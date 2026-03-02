using System.Diagnostics;
using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Hooks.BuiltIn;

/// <summary>
/// Specifies the audit logging level.
/// </summary>
public enum AuditLevel
{
    /// <summary>
    /// Log basic invocation info only.
    /// </summary>
    Basic,

    /// <summary>
    /// Log invocation info plus request arguments.
    /// </summary>
    Standard,

    /// <summary>
    /// Log invocation info, request arguments, and response content.
    /// </summary>
    Detailed
}

/// <summary>
/// Configuration for the audit hook.
/// </summary>
public sealed class AuditConfiguration
{
    /// <summary>
    /// Gets or sets the audit logging level.
    /// Default is Standard.
    /// </summary>
    public AuditLevel Level { get; set; } = AuditLevel.Standard;

    /// <summary>
    /// Gets or sets whether to include sensitive arguments in audit logs.
    /// Default is false.
    /// </summary>
    public bool IncludeSensitiveData { get; set; } = false;

    /// <summary>
    /// Gets or sets argument names that should be redacted (case-insensitive).
    /// Default includes common sensitive parameter names.
    /// </summary>
    public List<string> SensitiveArguments { get; set; } =
    [
        "password", "secret", "token", "key", "apikey", "api_key",
        "credential", "auth", "bearer", "private", "ssn", "credit_card"
    ];

    /// <summary>
    /// Gets or sets whether to log correlation IDs for tracking.
    /// Default is true.
    /// </summary>
    public bool IncludeCorrelationId { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum length of argument/response values to log.
    /// Default is 500 characters.
    /// </summary>
    public int MaxValueLength { get; set; } = 500;

    /// <summary>
    /// Gets or sets tools to exclude from auditing (supports wildcards).
    /// </summary>
    public List<string> ExcludeTools { get; set; } = [];

    /// <summary>
    /// Gets or sets tools to include in auditing (supports wildcards).
    /// If empty, all tools are included (except those in ExcludeTools).
    /// </summary>
    public List<string> IncludeTools { get; set; } = [];
}

/// <summary>
/// A pre-invoke and post-invoke hook that creates compliance audit trails for tool invocations.
/// Logs detailed information about who called what, when, and with what result.
/// </summary>
public sealed class AuditHook : IPreInvokeHook, IPostInvokeHook
{
    private readonly ILogger _logger;
    private readonly AuditConfiguration _config;
    private readonly HashSet<string> _sensitiveArgs;

    /// <summary>
    /// The key used to store the audit start time.
    /// </summary>
    public const string AuditStartTimeKey = "McpProxy.Audit.StartTime";

    /// <summary>
    /// The key used to store the correlation ID.
    /// </summary>
    public const string AuditCorrelationIdKey = "McpProxy.Audit.CorrelationId";

    /// <summary>
    /// Initializes a new instance of <see cref="AuditHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The audit configuration.</param>
    public AuditHook(ILogger logger, AuditConfiguration config)
    {
        _logger = logger;
        _config = config;
        _sensitiveArgs = new HashSet<string>(config.SensitiveArguments, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pre-invoke audit happens very early (priority -950) to capture the request before any transformation.
    /// Post-invoke audit happens very late (priority 950) to capture the final result.
    /// </remarks>
    public int Priority => -950;

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        // Check if this tool should be audited
        if (!ShouldAudit(context.ToolName))
        {
            return ValueTask.CompletedTask;
        }

        // Generate correlation ID
        var correlationId = _config.IncludeCorrelationId
            ? Activity.Current?.Id ?? Guid.NewGuid().ToString("N")[..12]
            : null;

        if (correlationId is not null)
        {
            context.Items[AuditCorrelationIdKey] = correlationId;
        }

        // Record start time
        context.Items[AuditStartTimeKey] = DateTimeOffset.UtcNow;

        var principalId = context.AuthenticationResult?.PrincipalId ?? "anonymous";

        // Log basic audit entry
        ProxyLogger.AuditEntry(_logger, "INVOKE", context.ToolName, context.ServerName, principalId);

        // Log additional details based on audit level
        if (_config.Level >= AuditLevel.Standard && context.Request?.Arguments is not null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var redactedArgs = RedactSensitiveArguments(context.Request.Arguments);
                var argsJson = JsonSerializer.Serialize(redactedArgs);
                var truncatedArgs = TruncateValue(argsJson);

                _logger.LogInformation(
                    "Audit [{CorrelationId}] Tool: {ToolName}, Server: {ServerName}, Principal: {PrincipalId}, Arguments: {Arguments}",
                    correlationId ?? "none",
                    context.ToolName,
                    context.ServerName,
                    principalId,
                    truncatedArgs);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        // Check if this tool should be audited
        if (!ShouldAudit(context.ToolName))
        {
            return ValueTask.FromResult(result);
        }

        // Calculate duration
        var durationMs = 0L;
        if (context.Items.TryGetValue(AuditStartTimeKey, out var startTimeObj) && startTimeObj is DateTimeOffset startTime)
        {
            durationMs = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
        }

        var correlationId = context.Items.TryGetValue(AuditCorrelationIdKey, out var corrId)
            ? corrId as string
            : null;

        var status = result.IsError == true ? "FAILED" : "SUCCESS";

        // Log completion
        ProxyLogger.AuditCompletion(_logger, context.ToolName, durationMs, status);

        // Log detailed result if configured
        if (_config.Level >= AuditLevel.Detailed && result.Content is not null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var resultText = ExtractResultText(result);
                var truncatedResult = TruncateValue(resultText);

                _logger.LogInformation(
                    "Audit [{CorrelationId}] Tool: {ToolName}, Duration: {DurationMs}ms, Status: {Status}, Result: {Result}",
                    correlationId ?? "none",
                    context.ToolName,
                    durationMs,
                    status,
                    truncatedResult);
            }
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Audit [{CorrelationId}] Tool: {ToolName}, Duration: {DurationMs}ms, Status: {Status}",
                    correlationId ?? "none",
                    context.ToolName,
                    durationMs,
                    status);
            }
        }

        return ValueTask.FromResult(result);
    }

    private bool ShouldAudit(string toolName)
    {
        // Check exclude list first
        if (_config.ExcludeTools.Count > 0 && _config.ExcludeTools.Any(p => MatchesPattern(toolName, p)))
        {
            return false;
        }

        // If include list is specified, tool must match
        if (_config.IncludeTools.Count > 0)
        {
            return _config.IncludeTools.Any(p => MatchesPattern(toolName, p));
        }

        // Default: audit all tools not in exclude list
        return true;
    }

    private static bool MatchesPattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, object?> RedactSensitiveArguments(IDictionary<string, JsonElement> arguments)
    {
        var redacted = new Dictionary<string, object?>();
        foreach (var (key, value) in arguments)
        {
            if (!_config.IncludeSensitiveData && _sensitiveArgs.Contains(key))
            {
                redacted[key] = "[REDACTED]";
            }
            else
            {
                redacted[key] = value;
            }
        }

        return redacted;
    }

    private static string ExtractResultText(CallToolResult result)
    {
        if (result.Content is null)
        {
            return string.Empty;
        }

        return string.Join(" ", result.Content
            .OfType<TextContentBlock>()
            .Where(c => c.Text is not null)
            .Select(c => c.Text));
    }

    private string TruncateValue(string value)
    {
        if (value.Length <= _config.MaxValueLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, _config.MaxValueLength), "...[truncated]");
    }
}

/// <summary>
/// Post-invoke version of AuditHook with higher priority (executes later).
/// This is returned when AuditHook is used as IPostInvokeHook.
/// </summary>
file sealed class AuditPostInvokeHook : IPostInvokeHook
{
    private readonly AuditHook _auditHook;

    public AuditPostInvokeHook(AuditHook auditHook)
    {
        _auditHook = auditHook;
    }

    /// <inheritdoc />
    public int Priority => 950; // Execute very late to capture final result

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        return _auditHook.OnPostInvokeAsync(context, result);
    }
}
