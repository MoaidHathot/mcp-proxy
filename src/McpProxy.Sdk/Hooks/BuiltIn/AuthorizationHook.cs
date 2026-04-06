using McpProxy.Abstractions;
using McpProxy.Sdk.Exceptions;
using McpProxy.Sdk.Logging;
using McpProxy.Sdk.Telemetry;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Sdk.Hooks.BuiltIn;

/// <summary>
/// Specifies how authorization policies are evaluated when multiple rules match.
/// </summary>
public enum AuthorizationMode
{
    /// <summary>
    /// All matching rules must grant access.
    /// </summary>
    AllOf,

    /// <summary>
    /// At least one matching rule must grant access.
    /// </summary>
    AnyOf
}

/// <summary>
/// Defines a single authorization rule.
/// </summary>
public sealed class AuthorizationRule
{
    /// <summary>
    /// Gets or sets the tool pattern to match (supports wildcards: "prefix_*", "*_suffix").
    /// If null or empty, matches all tools.
    /// </summary>
    public string? ToolPattern { get; set; }

    /// <summary>
    /// Gets or sets the server pattern to match (supports wildcards).
    /// If null or empty, matches all servers.
    /// </summary>
    public string? ServerPattern { get; set; }

    /// <summary>
    /// Gets or sets the required roles (any of these roles grants access).
    /// Roles are extracted from AuthenticationResult.Properties["roles"] (comma-separated).
    /// </summary>
    public string[] RequiredRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets the required scopes (any of these scopes grants access).
    /// Scopes are extracted from AuthenticationResult.Properties["scopes"] (comma-separated).
    /// </summary>
    public string[] RequiredScopes { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this rule allows or denies access when matched.
    /// Default is true (allow).
    /// </summary>
    public bool Allow { get; set; } = true;
}

/// <summary>
/// Configuration for the authorization hook.
/// </summary>
public sealed class AuthorizationConfiguration
{
    /// <summary>
    /// Gets or sets the default action when no rules match.
    /// Default is false (deny by default).
    /// </summary>
    public bool DefaultAllow { get; set; } = false;

    /// <summary>
    /// Gets or sets how multiple matching rules are evaluated.
    /// Default is AnyOf.
    /// </summary>
    public AuthorizationMode Mode { get; set; } = AuthorizationMode.AnyOf;

    /// <summary>
    /// Gets or sets whether to require authentication (non-null AuthenticationResult).
    /// Default is true.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// Gets or sets the authorization rules.
    /// </summary>
    public List<AuthorizationRule> Rules { get; set; } = [];

    /// <summary>
    /// Gets or sets the error message when authorization is denied.
    /// </summary>
    public string DeniedMessage { get; set; } = "Access denied. You do not have permission to invoke this tool.";
}

/// <summary>
/// A pre-invoke hook that enforces fine-grained authorization based on roles and scopes.
/// Integrates with Azure AD authentication by reading roles/scopes from AuthenticationResult.Properties.
/// </summary>
public sealed class AuthorizationHook : IPreInvokeHook
{
    private readonly ILogger _logger;
    private readonly ProxyMetrics? _metrics;
    private readonly AuthorizationConfiguration _config;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthorizationHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The authorization configuration.</param>
    /// <param name="metrics">Optional metrics instance for recording authorization events.</param>
    public AuthorizationHook(
        ILogger logger,
        AuthorizationConfiguration config,
        ProxyMetrics? metrics = null)
    {
        _logger = logger;
        _config = config;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public int Priority => -700; // Execute after rate limiting and timeout

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var principalId = context.AuthenticationResult?.PrincipalId;

        // Check if authentication is required
        if (_config.RequireAuthentication && context.AuthenticationResult is null)
        {
            var reason = "Authentication required";
            ProxyLogger.AuthorizationDenied(_logger, context.ToolName, principalId, reason);
            _metrics?.RecordAuthorizationDenied(context.ServerName, context.ToolName, reason);
            throw new AuthorizationException(context.ToolName, principalId, reason);
        }

        // Extract roles and scopes from authentication result
        var userRoles = ExtractValues(context.AuthenticationResult?.Properties, "roles");
        var userScopes = ExtractValues(context.AuthenticationResult?.Properties, "scopes");

        // Find matching rules
        var matchingRules = _config.Rules
            .Where(r => MatchesPattern(context.ToolName, r.ToolPattern) &&
                        MatchesPattern(context.ServerName, r.ServerPattern))
            .ToList();

        bool isAllowed;

        if (matchingRules.Count == 0)
        {
            // No rules match, use default
            isAllowed = _config.DefaultAllow;
        }
        else
        {
            // Evaluate matching rules
            isAllowed = _config.Mode switch
            {
                AuthorizationMode.AllOf => matchingRules.All(r => EvaluateRule(r, userRoles, userScopes)),
                AuthorizationMode.AnyOf => matchingRules.Any(r => EvaluateRule(r, userRoles, userScopes)),
                _ => _config.DefaultAllow
            };
        }

        if (isAllowed)
        {
            ProxyLogger.AuthorizationGranted(_logger, context.ToolName, principalId);
            _metrics?.RecordAuthorizationGranted(context.ServerName, context.ToolName);
        }
        else
        {
            var reason = BuildDenialReason(matchingRules, userRoles, userScopes);
            ProxyLogger.AuthorizationDenied(_logger, context.ToolName, principalId, reason);
            _metrics?.RecordAuthorizationDenied(context.ServerName, context.ToolName, reason);
            throw new AuthorizationException(context.ToolName, principalId, reason);
        }

        return ValueTask.CompletedTask;
    }

    private static HashSet<string> ExtractValues(IDictionary<string, string>? properties, string key)
    {
        if (properties is null || !properties.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool EvaluateRule(AuthorizationRule rule, HashSet<string> userRoles, HashSet<string> userScopes)
    {
        // If rule has no requirements, use its Allow value directly
        if (rule.RequiredRoles.Length == 0 && rule.RequiredScopes.Length == 0)
        {
            return rule.Allow;
        }

        // Check if user has any of the required roles
        var hasRequiredRole = rule.RequiredRoles.Length == 0 ||
                              rule.RequiredRoles.Any(r => userRoles.Contains(r));

        // Check if user has any of the required scopes
        var hasRequiredScope = rule.RequiredScopes.Length == 0 ||
                               rule.RequiredScopes.Any(s => userScopes.Contains(s));

        // User must satisfy both role and scope requirements (if specified)
        var satisfiesRequirements = hasRequiredRole && hasRequiredScope;

        // Apply the rule's allow/deny decision
        return rule.Allow ? satisfiesRequirements : !satisfiesRequirements;
    }

    private static bool MatchesPattern(string input, string? pattern)
    {
        // Null or empty pattern matches everything
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        // Trailing wildcard (prefix matching)
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Leading wildcard (suffix matching)
        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        // Exact match
        return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildDenialReason(List<AuthorizationRule> matchingRules, HashSet<string> userRoles, HashSet<string> userScopes)
    {
        if (matchingRules.Count == 0)
        {
            return "No authorization rules match this tool/server combination";
        }

        var requiredRoles = matchingRules
            .Where(r => r.Allow && r.RequiredRoles.Length > 0)
            .SelectMany(r => r.RequiredRoles)
            .Distinct()
            .ToList();

        var requiredScopes = matchingRules
            .Where(r => r.Allow && r.RequiredScopes.Length > 0)
            .SelectMany(r => r.RequiredScopes)
            .Distinct()
            .ToList();

        var reasons = new List<string>();

        if (requiredRoles.Count > 0)
        {
            reasons.Add($"requires one of roles: [{string.Join(", ", requiredRoles)}]");
        }

        if (requiredScopes.Count > 0)
        {
            reasons.Add($"requires one of scopes: [{string.Join(", ", requiredScopes)}]");
        }

        if (reasons.Count > 0)
        {
            return string.Join(" and ", reasons);
        }

        return _config.DeniedMessage;
    }
}
