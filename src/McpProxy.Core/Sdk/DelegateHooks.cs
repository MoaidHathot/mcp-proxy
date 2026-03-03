using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Sdk;

/// <summary>
/// A pre-invoke hook that delegates to a function.
/// </summary>
public sealed class DelegatePreInvokeHook : IPreInvokeHook
{
    private readonly Func<HookContext<CallToolRequestParams>, ValueTask> _handler;
    private readonly int _priority;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The handler function.</param>
    /// <param name="priority">The hook priority.</param>
    public DelegatePreInvokeHook(Func<HookContext<CallToolRequestParams>, ValueTask> handler, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _priority = priority;
    }

    /// <summary>
    /// Creates a hook from a synchronous action.
    /// </summary>
    /// <param name="handler">The handler action.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>A new hook instance.</returns>
    public static DelegatePreInvokeHook FromAction(Action<HookContext<CallToolRequestParams>> handler, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new DelegatePreInvokeHook(context =>
        {
            handler(context);
            return ValueTask.CompletedTask;
        }, priority);
    }

    /// <inheritdoc />
    public int Priority => _priority;

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        return _handler(context);
    }
}

/// <summary>
/// A post-invoke hook that delegates to a function.
/// </summary>
public sealed class DelegatePostInvokeHook : IPostInvokeHook
{
    private readonly Func<HookContext<CallToolRequestParams>, CallToolResult, ValueTask<CallToolResult>> _handler;
    private readonly int _priority;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The handler function.</param>
    /// <param name="priority">The hook priority.</param>
    public DelegatePostInvokeHook(
        Func<HookContext<CallToolRequestParams>, CallToolResult, ValueTask<CallToolResult>> handler,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _priority = priority;
    }

    /// <summary>
    /// Creates a hook from a synchronous function.
    /// </summary>
    /// <param name="handler">The handler function.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>A new hook instance.</returns>
    public static DelegatePostInvokeHook FromFunc(
        Func<HookContext<CallToolRequestParams>, CallToolResult, CallToolResult> handler,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new DelegatePostInvokeHook((context, result) => 
            ValueTask.FromResult(handler(context, result)), priority);
    }

    /// <inheritdoc />
    public int Priority => _priority;

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        return _handler(context, result);
    }
}

/// <summary>
/// A combined tool hook that delegates to functions.
/// </summary>
public sealed class DelegateToolHook : IToolHook
{
    private readonly Func<HookContext<CallToolRequestParams>, ValueTask>? _preInvokeHandler;
    private readonly Func<HookContext<CallToolRequestParams>, CallToolResult, ValueTask<CallToolResult>>? _postInvokeHandler;
    private readonly int _priority;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="preInvokeHandler">The pre-invoke handler.</param>
    /// <param name="postInvokeHandler">The post-invoke handler.</param>
    /// <param name="priority">The hook priority.</param>
    public DelegateToolHook(
        Func<HookContext<CallToolRequestParams>, ValueTask>? preInvokeHandler = null,
        Func<HookContext<CallToolRequestParams>, CallToolResult, ValueTask<CallToolResult>>? postInvokeHandler = null,
        int priority = 0)
    {
        _preInvokeHandler = preInvokeHandler;
        _postInvokeHandler = postInvokeHandler;
        _priority = priority;
    }

    /// <inheritdoc />
    public int Priority => _priority;

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        return _preInvokeHandler?.Invoke(context) ?? ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result)
    {
        return _postInvokeHandler?.Invoke(context, result) ?? ValueTask.FromResult(result);
    }
}

/// <summary>
/// A tool interceptor that delegates to a function.
/// </summary>
public sealed class DelegateToolInterceptor : IToolInterceptor
{
    private readonly Func<IEnumerable<ToolWithServer>, IEnumerable<ToolWithServer>> _handler;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The handler function.</param>
    public DelegateToolInterceptor(Func<IEnumerable<ToolWithServer>, IEnumerable<ToolWithServer>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc />
    public IEnumerable<ToolWithServer> InterceptTools(IEnumerable<ToolWithServer> tools)
    {
        return _handler(tools);
    }
}

/// <summary>
/// A tool call interceptor that delegates to a function.
/// </summary>
public sealed class DelegateToolCallInterceptor : IToolCallInterceptor
{
    private readonly Func<ToolCallContext, CancellationToken, ValueTask<CallToolResult?>> _handler;
    private readonly int _priority;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The handler function.</param>
    /// <param name="priority">The interceptor priority.</param>
    public DelegateToolCallInterceptor(
        Func<ToolCallContext, CancellationToken, ValueTask<CallToolResult?>> handler,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _priority = priority;
    }

    /// <inheritdoc />
    public int Priority => _priority;

    /// <inheritdoc />
    public ValueTask<CallToolResult?> InterceptAsync(ToolCallContext context, CancellationToken cancellationToken)
    {
        return _handler(context, cancellationToken);
    }
}

/// <summary>
/// A tool filter that delegates to a function.
/// </summary>
public sealed class DelegateToolFilter : IToolFilter
{
    private readonly Func<Tool, string, bool> _handler;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The filter function.</param>
    public DelegateToolFilter(Func<Tool, string, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc />
    public bool ShouldInclude(Tool tool, string serverName)
    {
        return _handler(tool, serverName);
    }
}

/// <summary>
/// A tool transformer that delegates to a function.
/// </summary>
public sealed class DelegateToolTransformer : IToolTransformer
{
    private readonly Func<Tool, string, Tool> _handler;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The transformer function.</param>
    public DelegateToolTransformer(Func<Tool, string, Tool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc />
    public Tool Transform(Tool tool, string serverName)
    {
        return _handler(tool, serverName);
    }
}
