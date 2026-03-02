using System.Diagnostics.Metrics;

namespace McpProxy.Core.Telemetry;

/// <summary>
/// OpenTelemetry metrics for the MCP proxy.
/// </summary>
public sealed class ProxyMetrics : IDisposable
{
    /// <summary>
    /// The name of the meter for MCP proxy metrics.
    /// </summary>
    public const string MeterName = "McpProxy";

    private readonly Meter _meter;
    private readonly Counter<long> _toolCallsTotal;
    private readonly Counter<long> _toolCallsSuccessful;
    private readonly Counter<long> _toolCallsFailed;
    private readonly Counter<long> _resourceReadsTotal;
    private readonly Counter<long> _promptGetsTotal;
    private readonly Histogram<double> _toolCallDuration;
    private readonly Histogram<double> _resourceReadDuration;
    private readonly Histogram<double> _promptGetDuration;
    private readonly UpDownCounter<int> _activeBackendConnections;

    // Hook-related metrics
    private readonly Counter<long> _rateLimitExceeded;
    private readonly Counter<long> _authorizationDenied;
    private readonly Counter<long> _authorizationGranted;
    private readonly Counter<long> _retryAttempts;
    private readonly Counter<long> _contentFilterTriggered;

    /// <summary>
    /// Initializes a new instance of <see cref="ProxyMetrics"/>.
    /// </summary>
    /// <param name="meterFactory">The meter factory.</param>
    public ProxyMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _toolCallsTotal = _meter.CreateCounter<long>(
            "mcpproxy.tool_calls.total",
            unit: "{calls}",
            description: "Total number of tool calls processed");

        _toolCallsSuccessful = _meter.CreateCounter<long>(
            "mcpproxy.tool_calls.successful",
            unit: "{calls}",
            description: "Number of successful tool calls");

        _toolCallsFailed = _meter.CreateCounter<long>(
            "mcpproxy.tool_calls.failed",
            unit: "{calls}",
            description: "Number of failed tool calls");

        _resourceReadsTotal = _meter.CreateCounter<long>(
            "mcpproxy.resource_reads.total",
            unit: "{reads}",
            description: "Total number of resource reads");

        _promptGetsTotal = _meter.CreateCounter<long>(
            "mcpproxy.prompt_gets.total",
            unit: "{gets}",
            description: "Total number of prompt gets");

        _toolCallDuration = _meter.CreateHistogram<double>(
            "mcpproxy.tool_call.duration",
            unit: "ms",
            description: "Duration of tool calls in milliseconds");

        _resourceReadDuration = _meter.CreateHistogram<double>(
            "mcpproxy.resource_read.duration",
            unit: "ms",
            description: "Duration of resource reads in milliseconds");

        _promptGetDuration = _meter.CreateHistogram<double>(
            "mcpproxy.prompt_get.duration",
            unit: "ms",
            description: "Duration of prompt gets in milliseconds");

        _activeBackendConnections = _meter.CreateUpDownCounter<int>(
            "mcpproxy.backend_connections.active",
            unit: "{connections}",
            description: "Number of active backend connections");

        // Hook-related metrics
        _rateLimitExceeded = _meter.CreateCounter<long>(
            "mcpproxy.hooks.rate_limit.exceeded",
            unit: "{events}",
            description: "Number of rate limit exceeded events");

        _authorizationDenied = _meter.CreateCounter<long>(
            "mcpproxy.hooks.authorization.denied",
            unit: "{events}",
            description: "Number of authorization denied events");

        _authorizationGranted = _meter.CreateCounter<long>(
            "mcpproxy.hooks.authorization.granted",
            unit: "{events}",
            description: "Number of authorization granted events");

        _retryAttempts = _meter.CreateCounter<long>(
            "mcpproxy.hooks.retry.attempts",
            unit: "{attempts}",
            description: "Number of retry attempts");

        _contentFilterTriggered = _meter.CreateCounter<long>(
            "mcpproxy.hooks.content_filter.triggered",
            unit: "{events}",
            description: "Number of content filter triggered events");
    }

    /// <summary>
    /// Records a tool call.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    public void RecordToolCall(string serverName, string toolName)
    {
        _toolCallsTotal.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName));
    }

    /// <summary>
    /// Records a successful tool call.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    public void RecordToolCallSuccess(string serverName, string toolName)
    {
        _toolCallsSuccessful.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName));
    }

    /// <summary>
    /// Records a failed tool call.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="errorType">The type of error.</param>
    public void RecordToolCallFailure(string serverName, string toolName, string errorType)
    {
        _toolCallsFailed.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("error_type", errorType));
    }

    /// <summary>
    /// Records tool call duration.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    public void RecordToolCallDuration(string serverName, string toolName, double durationMs)
    {
        _toolCallDuration.Record(durationMs,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName));
    }

    /// <summary>
    /// Records a resource read.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="resourceUri">The resource URI.</param>
    public void RecordResourceRead(string serverName, string resourceUri)
    {
        _resourceReadsTotal.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("resource", resourceUri));
    }

    /// <summary>
    /// Records resource read duration.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="resourceUri">The resource URI.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    public void RecordResourceReadDuration(string serverName, string resourceUri, double durationMs)
    {
        _resourceReadDuration.Record(durationMs,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("resource", resourceUri));
    }

    /// <summary>
    /// Records a prompt get.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="promptName">The prompt name.</param>
    public void RecordPromptGet(string serverName, string promptName)
    {
        _promptGetsTotal.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("prompt", promptName));
    }

    /// <summary>
    /// Records prompt get duration.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="promptName">The prompt name.</param>
    /// <param name="durationMs">The duration in milliseconds.</param>
    public void RecordPromptGetDuration(string serverName, string promptName, double durationMs)
    {
        _promptGetDuration.Record(durationMs,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("prompt", promptName));
    }

    /// <summary>
    /// Increments the active backend connections count.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    public void IncrementBackendConnections(string serverName)
    {
        _activeBackendConnections.Add(1,
            new KeyValuePair<string, object?>("server", serverName));
    }

    /// <summary>
    /// Decrements the active backend connections count.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    public void DecrementBackendConnections(string serverName)
    {
        _activeBackendConnections.Add(-1,
            new KeyValuePair<string, object?>("server", serverName));
    }

    /// <summary>
    /// Records a rate limit exceeded event.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="keyType">The rate limit key type.</param>
    public void RecordRateLimitExceeded(string serverName, string toolName, string keyType)
    {
        _rateLimitExceeded.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("key_type", keyType));
    }

    /// <summary>
    /// Records an authorization denied event.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="reason">The denial reason.</param>
    public void RecordAuthorizationDenied(string serverName, string toolName, string reason)
    {
        _authorizationDenied.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Records an authorization granted event.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    public void RecordAuthorizationGranted(string serverName, string toolName)
    {
        _authorizationGranted.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName));
    }

    /// <summary>
    /// Records a retry attempt.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="attempt">The attempt number.</param>
    public void RecordRetryAttempt(string serverName, string toolName, int attempt)
    {
        _retryAttempts.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("attempt", attempt));
    }

    /// <summary>
    /// Records a content filter triggered event.
    /// </summary>
    /// <param name="serverName">The backend server name.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="patternName">The pattern name that was triggered.</param>
    /// <param name="mode">The filter mode (redact, block, warn).</param>
    public void RecordContentFilterTriggered(string serverName, string toolName, string patternName, string mode)
    {
        _contentFilterTriggered.Add(1,
            new KeyValuePair<string, object?>("server", serverName),
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("pattern", patternName),
            new KeyValuePair<string, object?>("mode", mode));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
    }
}
