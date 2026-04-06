using System.Diagnostics;
using McpProxy.Abstractions;
using McpProxy.Sdk.Logging;
using McpProxy.Sdk.Telemetry;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Sdk.Hooks.BuiltIn;

/// <summary>
/// Configuration for the metrics hook.
/// </summary>
public sealed class MetricsHookConfiguration
{
    /// <summary>
    /// Gets or sets whether to record detailed timing metrics.
    /// Default is true.
    /// </summary>
    public bool RecordTiming { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to record request/response sizes.
    /// Default is true.
    /// </summary>
    public bool RecordSizes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include argument information in metrics tags.
    /// Default is false (for privacy/cardinality reasons).
    /// </summary>
    public bool IncludeArguments { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include principal ID in metrics tags.
    /// Default is true.
    /// </summary>
    public bool IncludePrincipalId { get; set; } = true;

    /// <summary>
    /// Gets or sets custom tags to add to all metrics.
    /// </summary>
    public Dictionary<string, string> CustomTags { get; set; } = [];
}

/// <summary>
/// A pre-invoke and post-invoke hook that records detailed metrics using OpenTelemetry.
/// Integrates with the existing ProxyMetrics infrastructure.
/// </summary>
public sealed class MetricsHook : IPreInvokeHook, IPostInvokeHook
{
    private readonly ILogger _logger;
    private readonly ProxyMetrics _metrics;
    private readonly MetricsHookConfiguration _config;

    /// <summary>
    /// The key used to store the stopwatch in the context Items dictionary.
    /// </summary>
    public const string StopwatchKey = "McpProxy.Metrics.Stopwatch";

    /// <summary>
    /// The key used to store the request size estimate.
    /// </summary>
    public const string RequestSizeKey = "McpProxy.Metrics.RequestSize";

    /// <summary>
    /// Initializes a new instance of <see cref="MetricsHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="metrics">The metrics instance.</param>
    /// <param name="config">The metrics configuration.</param>
    public MetricsHook(
        ILogger logger,
        ProxyMetrics metrics,
        MetricsHookConfiguration config)
    {
        _logger = logger;
        _metrics = metrics;
        _config = config;
    }

    /// <inheritdoc />
    /// <remarks>
    /// For IPreInvokeHook, execute late so we capture accurate start time after other hooks.
    /// For IPostInvokeHook, execute late so we capture the final result after all processing.
    /// </remarks>
    public int Priority => 900;

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        // Record the tool call
        _metrics.RecordToolCall(context.ServerName, context.ToolName);

        if (_config.RecordTiming)
        {
            // Start timing
            var stopwatch = Stopwatch.StartNew();
            context.Items[StopwatchKey] = stopwatch;
        }

        if (_config.RecordSizes)
        {
            // Estimate request size based on arguments
            var requestSize = EstimateRequestSize(context.Request);
            context.Items[RequestSizeKey] = requestSize;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        double durationMs = 0;

        if (_config.RecordTiming)
        {
            // Stop timing
            if (context.Items.TryGetValue(StopwatchKey, out var stopwatchObj) && stopwatchObj is Stopwatch stopwatch)
            {
                stopwatch.Stop();
                durationMs = stopwatch.Elapsed.TotalMilliseconds;

                // Record duration via ProxyMetrics
                _metrics.RecordToolCallDuration(context.ServerName, context.ToolName, durationMs);
            }
        }

        // Determine success or failure
        var isSuccess = result.IsError != true;

        if (isSuccess)
        {
            _metrics.RecordToolCallSuccess(context.ServerName, context.ToolName);
        }
        else
        {
            var errorType = DetermineErrorType(result);
            _metrics.RecordToolCallFailure(context.ServerName, context.ToolName, errorType);
        }

        // Log metrics for debugging
        ProxyLogger.MetricsRecorded(_logger, context.ToolName, durationMs, isSuccess);

        return ValueTask.FromResult(result);
    }

    private static int EstimateRequestSize(CallToolRequestParams? request)
    {
        if (request?.Arguments is null)
        {
            return 0;
        }

        // Rough estimate: sum of all argument values' string lengths
        var size = 0;
        foreach (var (key, value) in request.Arguments)
        {
            size += key.Length;
            size += value.GetRawText().Length;
        }

        return size;
    }

    private static string DetermineErrorType(CallToolResult? result)
    {
        if (result?.Content is null)
        {
            return "unknown";
        }

        var errorText = string.Join(" ", result.Content
            .OfType<TextContentBlock>()
            .Where(c => c.Text is not null)
            .Select(c => c.Text!.ToLowerInvariant()));

        if (errorText.Contains("timeout"))
        {
            return "timeout";
        }

        if (errorText.Contains("connection") || errorText.Contains("network"))
        {
            return "connection";
        }

        if (errorText.Contains("unauthorized") || errorText.Contains("authentication"))
        {
            return "authentication";
        }

        if (errorText.Contains("forbidden") || errorText.Contains("permission"))
        {
            return "authorization";
        }

        if (errorText.Contains("not found"))
        {
            return "not_found";
        }

        if (errorText.Contains("rate limit") || errorText.Contains("throttl"))
        {
            return "rate_limited";
        }

        if (errorText.Contains("validation") || errorText.Contains("invalid"))
        {
            return "validation";
        }

        return "error";
    }
}
