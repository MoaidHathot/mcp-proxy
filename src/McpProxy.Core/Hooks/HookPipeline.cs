using McpProxy.Abstractions;
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
    private readonly List<IPreInvokeHook> _preInvokeHooks = [];
    private readonly List<IPostInvokeHook> _postInvokeHooks = [];

    /// <summary>
    /// Initializes a new instance of <see cref="HookPipeline"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public HookPipeline(ILogger<HookPipeline> logger)
    {
        _logger = logger;
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

        foreach (var hook in _preInvokeHooks)
        {
            try
            {
                await hook.OnPreInvokeAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ProxyLogger.HookFailed(_logger, hook.GetType().Name, context.ToolName, ex);
                throw;
            }
        }
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

        foreach (var hook in _postInvokeHooks)
        {
            try
            {
                result = await hook.OnPostInvokeAsync(context, result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ProxyLogger.HookFailed(_logger, hook.GetType().Name, context.ToolName, ex);
                throw;
            }
        }

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
