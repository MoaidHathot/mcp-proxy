using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Logging;
using Microsoft.Extensions.Logging;

namespace McpProxy.Sdk.Debugging;

/// <summary>
/// Implementation of <see cref="IHookTracer"/> that logs hook execution details.
/// </summary>
public sealed class HookTracer : IHookTracer
{
    private readonly ILogger<HookTracer> _logger;
    private readonly bool _includeHookTiming;

    /// <summary>
    /// Initializes a new instance of <see cref="HookTracer"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The debug configuration.</param>
    public HookTracer(ILogger<HookTracer> logger, DebugConfiguration config)
    {
        _logger = logger;
        _includeHookTiming = config.IncludeHookTiming;
    }

    /// <inheritdoc/>
    public HookTraceContext BeginTrace(string toolName, string serverName)
    {
        var context = new HookTraceContext
        {
            ToolName = toolName,
            ServerName = serverName
        };

        ProxyLogger.HookTraceStarted(_logger, toolName, serverName);
        return context;
    }

    /// <inheritdoc/>
    public void RecordHookStart(HookTraceContext context, string hookName, string hookType, int priority)
    {
        var entry = new HookTraceEntry(
            HookName: hookName,
            HookType: hookType,
            Priority: priority,
            DurationMs: null,
            Status: "Executing",
            Error: null);

        context.Entries.Add(entry);
        ProxyLogger.HookExecuting(_logger, hookName, hookType, priority);
    }

    /// <inheritdoc/>
    public void RecordHookComplete(HookTraceContext context, string hookName, TimeSpan duration)
    {
        // Find the entry and update it
        var index = context.Entries.FindIndex(e => e.HookName == hookName && e.Status == "Executing");
        if (index >= 0)
        {
            var entry = context.Entries[index];
            context.Entries[index] = entry with
            {
                DurationMs = duration.TotalMilliseconds,
                Status = "Completed"
            };
        }

        if (_includeHookTiming)
        {
            ProxyLogger.HookCompleted(_logger, hookName, duration.TotalMilliseconds);
        }
    }

    /// <inheritdoc/>
    public void RecordHookFailed(HookTraceContext context, string hookName, Exception exception)
    {
        // Find the entry and update it
        var index = context.Entries.FindIndex(e => e.HookName == hookName && e.Status == "Executing");
        if (index >= 0)
        {
            var entry = context.Entries[index];
            context.Entries[index] = entry with
            {
                Status = "Failed",
                Error = exception.Message
            };
        }

        ProxyLogger.HookTraceFailed(_logger, hookName, exception.GetType().Name, exception.Message);
    }

    /// <inheritdoc/>
    public void EndTrace(HookTraceContext context)
    {
        var totalDurationMs = (DateTimeOffset.UtcNow - context.StartTime).TotalMilliseconds;
        var completedCount = context.Entries.Count(e => e.Status == "Completed");
        var failedCount = context.Entries.Count(e => e.Status == "Failed");

        ProxyLogger.HookTraceSummary(
            _logger,
            context.ToolName,
            context.ServerName,
            context.Entries.Count,
            completedCount,
            failedCount,
            totalDurationMs);
    }
}

/// <summary>
/// No-op implementation of <see cref="IHookTracer"/> used when tracing is disabled.
/// </summary>
public sealed class NullHookTracer : IHookTracer
{
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static readonly NullHookTracer Instance = new();

    private static readonly HookTraceContext s_emptyContext = new()
    {
        ToolName = string.Empty,
        ServerName = string.Empty
    };

    private NullHookTracer() { }

    /// <inheritdoc/>
    public HookTraceContext BeginTrace(string toolName, string serverName) => s_emptyContext;

    /// <inheritdoc/>
    public void RecordHookStart(HookTraceContext context, string hookName, string hookType, int priority) { }

    /// <inheritdoc/>
    public void RecordHookComplete(HookTraceContext context, string hookName, TimeSpan duration) { }

    /// <inheritdoc/>
    public void RecordHookFailed(HookTraceContext context, string hookName, Exception exception) { }

    /// <inheritdoc/>
    public void EndTrace(HookTraceContext context) { }
}
