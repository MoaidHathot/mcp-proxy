using System.Diagnostics;
using McpProxy.Abstractions;
using McpProxy.Core.Debugging;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Hooks;

/// <summary>
/// Manages the execution of pre-invoke and post-invoke hooks.
/// </summary>
public sealed class HookPipeline
{
    private readonly ILogger<HookPipeline> _logger;
    private readonly IHookTracer _tracer;
    private readonly List<IPreInvokeHook> _preInvokeHooks = [];
    private readonly List<IPostInvokeHook> _postInvokeHooks = [];

    /// <summary>
    /// Initializes a new instance of <see cref="HookPipeline"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tracer">Optional hook tracer for debugging. If null, tracing is disabled.</param>
    public HookPipeline(ILogger<HookPipeline> logger, IHookTracer? tracer = null)
    {
        _logger = logger;
        _tracer = tracer ?? NullHookTracer.Instance;
    }

    /// <summary>
    /// Adds a pre-invoke hook to the pipeline.
    /// </summary>
    /// <param name="hook">The hook to add.</param>
    public void AddPreInvokeHook(IPreInvokeHook hook)
    {
        _preInvokeHooks.Add(hook);
        _preInvokeHooks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Adds a post-invoke hook to the pipeline.
    /// </summary>
    /// <param name="hook">The hook to add.</param>
    public void AddPostInvokeHook(IPostInvokeHook hook)
    {
        _postInvokeHooks.Add(hook);
        _postInvokeHooks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Adds a combined hook (both pre and post) to the pipeline.
    /// </summary>
    /// <param name="hook">The hook to add.</param>
    public void AddHook(IToolHook hook)
    {
        AddPreInvokeHook(hook);
        AddPostInvokeHook(hook);
    }

    /// <summary>
    /// Executes all pre-invoke hooks.
    /// </summary>
    /// <param name="context">The hook context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask ExecutePreInvokeHooksAsync(HookContext<CallToolRequestParams> context)
    {
        if (_preInvokeHooks.Count == 0)
        {
            return;
        }

        ProxyLogger.ExecutingPreInvokeHooks(_logger, _preInvokeHooks.Count, context.ToolName);

        var traceContext = _tracer.BeginTrace(context.ToolName, context.ServerName);

        foreach (var hook in _preInvokeHooks)
        {
            var hookName = hook.GetType().Name;
            _tracer.RecordHookStart(traceContext, hookName, "PreInvoke", hook.Priority);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await hook.OnPreInvokeAsync(context).ConfigureAwait(false);
                stopwatch.Stop();
                _tracer.RecordHookComplete(traceContext, hookName, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _tracer.RecordHookFailed(traceContext, hookName, ex);
                ProxyLogger.HookFailed(_logger, hookName, context.ToolName, ex);
                _tracer.EndTrace(traceContext);
                throw;
            }
        }

        _tracer.EndTrace(traceContext);
    }

    /// <summary>
    /// Executes all post-invoke hooks.
    /// </summary>
    /// <param name="context">The hook context.</param>
    /// <param name="result">The result from the tool invocation.</param>
    /// <returns>The potentially modified result.</returns>
    public async ValueTask<CallToolResult> ExecutePostInvokeHooksAsync(
        HookContext<CallToolRequestParams> context,
        CallToolResult result)
    {
        if (_postInvokeHooks.Count == 0)
        {
            return result;
        }

        ProxyLogger.ExecutingPostInvokeHooks(_logger, _postInvokeHooks.Count, context.ToolName);

        var traceContext = _tracer.BeginTrace(context.ToolName, context.ServerName);

        foreach (var hook in _postInvokeHooks)
        {
            var hookName = hook.GetType().Name;
            _tracer.RecordHookStart(traceContext, hookName, "PostInvoke", hook.Priority);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                result = await hook.OnPostInvokeAsync(context, result).ConfigureAwait(false);
                stopwatch.Stop();
                _tracer.RecordHookComplete(traceContext, hookName, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _tracer.RecordHookFailed(traceContext, hookName, ex);
                ProxyLogger.HookFailed(_logger, hookName, context.ToolName, ex);
                _tracer.EndTrace(traceContext);
                throw;
            }
        }

        _tracer.EndTrace(traceContext);
        return result;
    }

    /// <summary>
    /// Gets the number of pre-invoke hooks.
    /// </summary>
    public int PreInvokeHookCount => _preInvokeHooks.Count;

    /// <summary>
    /// Gets the number of post-invoke hooks.
    /// </summary>
    public int PostInvokeHookCount => _postInvokeHooks.Count;
}
