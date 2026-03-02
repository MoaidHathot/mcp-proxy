using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.Core.Logging;
using McpProxy.Core.Telemetry;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Hooks.BuiltIn;

/// <summary>
/// Configuration for the retry hook.
/// </summary>
public sealed class RetryConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial delay in milliseconds before the first retry.
    /// Default is 100ms.
    /// </summary>
    public int InitialDelayMs { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum delay in milliseconds between retries.
    /// Default is 5000ms (5 seconds).
    /// </summary>
    public int MaxDelayMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the backoff multiplier for exponential backoff.
    /// Default is 2.0.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets whether to add jitter to delay calculations.
    /// Default is true.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets the error patterns that should trigger a retry (regex patterns).
    /// If empty, all errors trigger a retry.
    /// </summary>
    public List<string> RetryablePatterns { get; set; } = ["timeout", "connection", "unavailable", "temporary", "transient", "503", "429"];

    /// <summary>
    /// Gets or sets patterns that should NOT trigger a retry (regex patterns).
    /// These take precedence over RetryablePatterns.
    /// </summary>
    public List<string> NonRetryablePatterns { get; set; } = ["invalid", "unauthorized", "forbidden", "not found", "bad request", "400", "401", "403", "404"];
}

/// <summary>
/// A post-invoke hook that handles automatic retry for transient failures.
/// Uses exponential backoff with optional jitter.
/// Sets a flag in context.Items for the proxy server to handle the actual retry.
/// </summary>
public sealed class RetryHook : IPostInvokeHook
{
    private readonly ILogger _logger;
    private readonly ProxyMetrics? _metrics;
    private readonly RetryConfiguration _config;
    private readonly List<Regex> _retryableRegexes;
    private readonly List<Regex> _nonRetryableRegexes;

    /// <summary>
    /// The key used to store the retry request flag in the context Items dictionary.
    /// </summary>
    public const string RetryRequestedKey = "McpProxy.Retry.Requested";

    /// <summary>
    /// The key used to store the retry delay in milliseconds.
    /// </summary>
    public const string RetryDelayMsKey = "McpProxy.Retry.DelayMs";

    /// <summary>
    /// The key used to store the current retry attempt number.
    /// </summary>
    public const string RetryAttemptKey = "McpProxy.Retry.Attempt";

    /// <summary>
    /// The key used to store the matched pattern name for logging.
    /// </summary>
    public const string RetryPatternKey = "McpProxy.Retry.Pattern";

    /// <summary>
    /// Initializes a new instance of <see cref="RetryHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The retry configuration.</param>
    /// <param name="metrics">Optional metrics instance for recording retry events.</param>
    public RetryHook(
        ILogger logger,
        RetryConfiguration config,
        ProxyMetrics? metrics = null)
    {
        _logger = logger;
        _config = config;
        _metrics = metrics;

        // Pre-compile regex patterns for performance
        _retryableRegexes = config.RetryablePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();

        _nonRetryableRegexes = config.NonRetryablePatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();
    }

    /// <inheritdoc />
    public int Priority => 100; // Execute after tool invocation, early in post-invoke chain

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        // Check if result indicates an error
        if (!IsErrorResult(result))
        {
            return ValueTask.FromResult(result);
        }

        // Get current attempt count
        var currentAttempt = 0;
        if (context.Items.TryGetValue(RetryAttemptKey, out var attemptObj) && attemptObj is int attempt)
        {
            currentAttempt = attempt;
        }

        // Check if we've exhausted retries
        if (currentAttempt >= _config.MaxRetries)
        {
            ProxyLogger.RetryExhausted(_logger, context.ToolName, _config.MaxRetries);
            return ValueTask.FromResult(result);
        }

        // Extract error message for pattern matching
        var errorMessage = ExtractErrorMessage(result);

        // Check if error is non-retryable (takes precedence)
        if (IsNonRetryableError(errorMessage))
        {
            return ValueTask.FromResult(result);
        }

        // Check if error matches a retryable pattern
        var matchedPattern = GetMatchedRetryablePattern(errorMessage);
        if (matchedPattern is null && _retryableRegexes.Count > 0)
        {
            // Has patterns configured but none matched
            return ValueTask.FromResult(result);
        }

        // Calculate delay with exponential backoff and optional jitter
        var delay = CalculateDelay(currentAttempt);
        var nextAttempt = currentAttempt + 1;

        // Set retry request in context for proxy server to handle
        context.Items[RetryRequestedKey] = true;
        context.Items[RetryDelayMsKey] = delay;
        context.Items[RetryAttemptKey] = nextAttempt;
        context.Items[RetryPatternKey] = matchedPattern ?? "default";

        ProxyLogger.RetryRequested(_logger, context.ToolName, matchedPattern ?? "default");
        ProxyLogger.RetryAttempt(_logger, nextAttempt, _config.MaxRetries, context.ToolName, delay);
        _metrics?.RecordRetryAttempt(context.ServerName, context.ToolName, nextAttempt);

        return ValueTask.FromResult(result);
    }

    private static bool IsErrorResult(CallToolResult result)
    {
        // Check if the result indicates an error
        return result.IsError == true ||
               (result.Content?.OfType<TextContentBlock>().Any(c =>
                   c.Text?.Contains("error", StringComparison.OrdinalIgnoreCase) == true) ?? false);
    }

    private static string ExtractErrorMessage(CallToolResult result)
    {
        // Combine all text content for error analysis
        if (result.Content is null)
        {
            return string.Empty;
        }

        return string.Join(" ", result.Content
            .OfType<TextContentBlock>()
            .Where(c => c.Text is not null)
            .Select(c => c.Text));
    }

    private bool IsNonRetryableError(string errorMessage)
    {
        return _nonRetryableRegexes.Any(r => r.IsMatch(errorMessage));
    }

    private string? GetMatchedRetryablePattern(string errorMessage)
    {
        if (_retryableRegexes.Count == 0)
        {
            // No patterns configured, retry all errors
            return "all-errors";
        }

        foreach (var (regex, index) in _retryableRegexes.Select((r, i) => (r, i)))
        {
            if (regex.IsMatch(errorMessage))
            {
                return _config.RetryablePatterns[index];
            }
        }

        return null;
    }

    private int CalculateDelay(int currentAttempt)
    {
        // Exponential backoff: initialDelay * (multiplier ^ attempt)
        var delay = _config.InitialDelayMs * Math.Pow(_config.BackoffMultiplier, currentAttempt);

        // Apply jitter if configured (±25%)
        if (_config.UseJitter)
        {
#pragma warning disable CA5394 // Random is acceptable here - jitter is not security-sensitive
            var jitterFactor = 0.75 + (Random.Shared.NextDouble() * 0.5); // 0.75 to 1.25
#pragma warning restore CA5394
            delay *= jitterFactor;
        }

        // Cap at max delay
        return (int)Math.Min(delay, _config.MaxDelayMs);
    }
}
