using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.SDK.Logging;
using McpProxy.SDK.Telemetry;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Hooks.BuiltIn;

/// <summary>
/// Specifies the action to take when a content filter pattern matches.
/// </summary>
public enum ContentFilterMode
{
    /// <summary>
    /// Block the entire response and return an error.
    /// </summary>
    Block,

    /// <summary>
    /// Redact the matched content with a placeholder.
    /// </summary>
    Redact,

    /// <summary>
    /// Log a warning but allow the content through.
    /// </summary>
    Warn
}

/// <summary>
/// Defines a content filter pattern.
/// </summary>
public sealed class ContentFilterPattern
{
    /// <summary>
    /// Gets or sets the name of this filter pattern (for logging/metrics).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the regex pattern to match.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the action to take when this pattern matches.
    /// Default is Block.
    /// </summary>
    public ContentFilterMode Mode { get; set; } = ContentFilterMode.Block;

    /// <summary>
    /// Gets or sets the replacement text for Redact mode.
    /// Default is "[FILTERED]".
    /// </summary>
    public string RedactReplacement { get; set; } = "[FILTERED]";

    /// <summary>
    /// Gets or sets the error message for Block mode.
    /// </summary>
    public string BlockMessage { get; set; } = "Content blocked by security policy.";

    /// <summary>
    /// Gets or sets whether this pattern is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets tools this pattern applies to (supports wildcards).
    /// If empty, applies to all tools.
    /// </summary>
    public List<string> AppliesTo { get; set; } = [];
}

/// <summary>
/// Configuration for the content filter hook.
/// </summary>
public sealed class ContentFilterConfiguration
{
    /// <summary>
    /// Gets or sets the filter patterns.
    /// </summary>
    public List<ContentFilterPattern> Patterns { get; set; } = [];

    /// <summary>
    /// Gets or sets default patterns for common sensitive data.
    /// Default is true.
    /// </summary>
    public bool UseDefaultPatterns { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to scan request arguments in addition to responses.
    /// Default is false.
    /// </summary>
    public bool ScanRequests { get; set; } = false;
}

/// <summary>
/// A pre-invoke and post-invoke hook that filters prohibited content from tool responses.
/// Can block, redact, or warn on matching content based on configuration.
/// </summary>
public sealed class ContentFilterHook : IPreInvokeHook, IPostInvokeHook
{
    private readonly ILogger _logger;
    private readonly ProxyMetrics? _metrics;
    private readonly ContentFilterConfiguration _config;
    private readonly List<(ContentFilterPattern Pattern, Regex Regex)> _compiledPatterns;

    // Default patterns for common sensitive data
    private static readonly List<ContentFilterPattern> s_defaultPatterns =
    [
        new ContentFilterPattern
        {
            Name = "credit_card",
            Pattern = @"\b(?:\d{4}[- ]?){3}\d{4}\b",
            Mode = ContentFilterMode.Redact,
            RedactReplacement = "[CREDIT-CARD-REDACTED]"
        },
        new ContentFilterPattern
        {
            Name = "ssn",
            Pattern = @"\b\d{3}-\d{2}-\d{4}\b",
            Mode = ContentFilterMode.Redact,
            RedactReplacement = "[SSN-REDACTED]"
        },
        new ContentFilterPattern
        {
            Name = "email",
            Pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            Mode = ContentFilterMode.Warn
        },
        new ContentFilterPattern
        {
            Name = "api_key",
            Pattern = @"\b(?:api[_-]?key|apikey|access[_-]?token)[:\s]*['""]?([A-Za-z0-9_-]{20,})['""]?",
            Mode = ContentFilterMode.Redact,
            RedactReplacement = "[API-KEY-REDACTED]"
        },
        new ContentFilterPattern
        {
            Name = "bearer_token",
            Pattern = @"Bearer\s+[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+",
            Mode = ContentFilterMode.Redact,
            RedactReplacement = "[BEARER-TOKEN-REDACTED]"
        },
        new ContentFilterPattern
        {
            Name = "private_key",
            Pattern = @"-----BEGIN (?:RSA |EC )?PRIVATE KEY-----",
            Mode = ContentFilterMode.Block,
            BlockMessage = "Private key material detected and blocked."
        },
        new ContentFilterPattern
        {
            Name = "connection_string",
            Pattern = @"(?:Server|Data Source|Host)=[^;]+;.*(?:Password|Pwd)=[^;]+",
            Mode = ContentFilterMode.Redact,
            RedactReplacement = "[CONNECTION-STRING-REDACTED]"
        }
    ];

    /// <summary>
    /// Initializes a new instance of <see cref="ContentFilterHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The content filter configuration.</param>
    /// <param name="metrics">Optional metrics instance for recording filter events.</param>
    public ContentFilterHook(
        ILogger logger,
        ContentFilterConfiguration config,
        ProxyMetrics? metrics = null)
    {
        _logger = logger;
        _config = config;
        _metrics = metrics;

        // Compile all patterns
        _compiledPatterns = [];

        // Add default patterns if enabled
        if (config.UseDefaultPatterns)
        {
            foreach (var pattern in s_defaultPatterns)
            {
                if (pattern.Enabled)
                {
                    _compiledPatterns.Add((pattern, new Regex(pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)));
                }
            }
        }

        // Add custom patterns
        foreach (var pattern in config.Patterns)
        {
            if (pattern.Enabled && !string.IsNullOrEmpty(pattern.Pattern))
            {
                try
                {
                    _compiledPatterns.Add((pattern, new Regex(pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Invalid regex pattern '{PatternName}': {Pattern}", pattern.Name, pattern.Pattern);
                }
            }
        }
    }

    /// <inheritdoc />
    public int Priority => 800; // Execute late, after other processing

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        if (!_config.ScanRequests || context.Request?.Arguments is null)
        {
            return ValueTask.CompletedTask;
        }

        // Scan request arguments
        foreach (var (key, value) in context.Request.Arguments)
        {
            // Only scan string values
            if (value.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                continue;
            }

            var stringValue = value.GetString();
            if (stringValue is null)
            {
                continue;
            }

            foreach (var (pattern, regex) in GetApplicablePatterns(context.ToolName))
            {
                if (regex.IsMatch(stringValue))
                {
                    ProxyLogger.ContentFilterTriggered(_logger, context.ToolName, pattern.Name, pattern.Mode.ToString());
                    _metrics?.RecordContentFilterTriggered(context.ServerName, context.ToolName, pattern.Name, pattern.Mode.ToString());

                    if (pattern.Mode == ContentFilterMode.Block)
                    {
                        ProxyLogger.ContentBlocked(_logger, context.ToolName, pattern.BlockMessage);
                        throw new InvalidOperationException($"Request blocked: {pattern.BlockMessage}");
                    }

                    // For Redact mode on requests, we can only log/warn here since Arguments is read-only
                    // The caller would need to use InputTransformHook for actual modification
                    if (pattern.Mode == ContentFilterMode.Redact)
                    {
                        _logger.LogWarning("Content filter would redact argument '{Key}' but cannot modify request. Consider using InputTransformHook.", key);
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        if (result.Content is null)
        {
            return ValueTask.FromResult(result);
        }

        var contentModified = false;
        var newContent = new List<ContentBlock>();

        foreach (var content in result.Content)
        {
            if (content is not TextContentBlock textContent || textContent.Text is null)
            {
                newContent.Add(content);
                continue;
            }

            var text = textContent.Text;
            var blocked = false;

            foreach (var (pattern, regex) in GetApplicablePatterns(context.ToolName))
            {
                if (!regex.IsMatch(text))
                {
                    continue;
                }

                ProxyLogger.ContentFilterTriggered(_logger, context.ToolName, pattern.Name, pattern.Mode.ToString());
                _metrics?.RecordContentFilterTriggered(context.ServerName, context.ToolName, pattern.Name, pattern.Mode.ToString());

                switch (pattern.Mode)
                {
                    case ContentFilterMode.Block:
                        ProxyLogger.ContentBlocked(_logger, context.ToolName, pattern.BlockMessage);
                        blocked = true;
                        break;

                    case ContentFilterMode.Redact:
                        text = regex.Replace(text, pattern.RedactReplacement);
                        contentModified = true;
                        break;

                    case ContentFilterMode.Warn:
                        // Just log, don't modify
                        break;
                }

                if (blocked)
                {
                    break;
                }
            }

            if (blocked)
            {
                // Return blocked error result
                return ValueTask.FromResult(new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = "Content blocked by security policy. The response contained prohibited content."
                        }
                    ]
                });
            }

            if (contentModified)
            {
                newContent.Add(new TextContentBlock
                {
                    Text = text,
                    Annotations = textContent.Annotations
                });
            }
            else
            {
                newContent.Add(content);
            }
        }

        if (contentModified)
        {
            return ValueTask.FromResult(new CallToolResult
            {
                IsError = result.IsError,
                Content = newContent
            });
        }

        return ValueTask.FromResult(result);
    }

    private IEnumerable<(ContentFilterPattern Pattern, Regex Regex)> GetApplicablePatterns(string toolName)
    {
        foreach (var (pattern, regex) in _compiledPatterns)
        {
            // If no AppliesTo specified, pattern applies to all tools
            if (pattern.AppliesTo.Count == 0)
            {
                yield return (pattern, regex);
                continue;
            }

            // Check if tool matches any of the AppliesTo patterns
            if (pattern.AppliesTo.Any(p => MatchesTool(toolName, p)))
            {
                yield return (pattern, regex);
            }
        }
    }

    private static bool MatchesTool(string toolName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return toolName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(toolName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
