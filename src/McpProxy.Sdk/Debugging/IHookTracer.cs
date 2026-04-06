namespace McpProxy.Sdk.Debugging;

/// <summary>
/// Traces hook execution for debugging purposes.
/// </summary>
public interface IHookTracer
{
    /// <summary>
    /// Begins tracing a hook pipeline execution.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="serverName">The name of the server handling the request.</param>
    /// <returns>A context object for tracking the trace session.</returns>
    HookTraceContext BeginTrace(string toolName, string serverName);

    /// <summary>
    /// Records when a hook starts executing.
    /// </summary>
    /// <param name="context">The trace context.</param>
    /// <param name="hookName">The name of the hook.</param>
    /// <param name="hookType">The type of hook (PreInvoke or PostInvoke).</param>
    /// <param name="priority">The hook priority.</param>
    void RecordHookStart(HookTraceContext context, string hookName, string hookType, int priority);

    /// <summary>
    /// Records when a hook completes successfully.
    /// </summary>
    /// <param name="context">The trace context.</param>
    /// <param name="hookName">The name of the hook.</param>
    /// <param name="duration">The execution duration.</param>
    void RecordHookComplete(HookTraceContext context, string hookName, TimeSpan duration);

    /// <summary>
    /// Records when a hook fails with an exception.
    /// </summary>
    /// <param name="context">The trace context.</param>
    /// <param name="hookName">The name of the hook.</param>
    /// <param name="exception">The exception that occurred.</param>
    void RecordHookFailed(HookTraceContext context, string hookName, Exception exception);

    /// <summary>
    /// Ends tracing and logs the summary.
    /// </summary>
    /// <param name="context">The trace context.</param>
    void EndTrace(HookTraceContext context);
}

/// <summary>
/// Context for tracking a single hook trace session.
/// </summary>
public sealed class HookTraceContext
{
    /// <summary>
    /// Gets the name of the tool being invoked.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the name of the server handling the request.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets the time when the trace started.
    /// </summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the list of trace entries.
    /// </summary>
    public List<HookTraceEntry> Entries { get; } = [];
}

/// <summary>
/// A single entry in a hook trace.
/// </summary>
/// <param name="HookName">The name of the hook.</param>
/// <param name="HookType">The type of hook (PreInvoke or PostInvoke).</param>
/// <param name="Priority">The hook priority.</param>
/// <param name="DurationMs">The execution duration in milliseconds, if completed.</param>
/// <param name="Status">The execution status (Executing, Completed, Failed).</param>
/// <param name="Error">The error message, if failed.</param>
public sealed record HookTraceEntry(
    string HookName,
    string HookType,
    int Priority,
    double? DurationMs,
    string Status,
    string? Error);
