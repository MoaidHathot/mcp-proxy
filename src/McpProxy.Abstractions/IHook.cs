using ModelContextProtocol.Protocol;

namespace McpProxy.Abstractions;

/// <summary>
/// Context information passed to hooks during tool invocation.
/// </summary>
/// <typeparam name="TRequest">The type of the request parameters.</typeparam>
public sealed class HookContext<TRequest>
{
    /// <summary>
    /// Gets the name of the MCP server handling this request.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets or sets the request parameters. Can be modified by pre-invoke hooks.
    /// </summary>
    public required TRequest Request { get; set; }

    /// <summary>
    /// Gets the name of the tool being invoked.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets a dictionary for sharing data between hooks in the pipeline.
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// Gets or sets the cancellation token for the operation.
    /// Can be modified by timeout hooks to enforce time limits.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Gets the authentication result from the current request, if available.
    /// Contains principal identity, roles, scopes, and other claims from authentication.
    /// </summary>
    public AuthenticationResult? AuthenticationResult { get; init; }
}

/// <summary>
/// Interface for hooks that execute before a tool is invoked.
/// </summary>
public interface IPreInvokeHook
{
    /// <summary>
    /// Gets the priority of this hook. Lower values execute first.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Called before a tool is invoked. Can modify the request parameters.
    /// </summary>
    /// <param name="context">The hook context containing request information.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context);
}

/// <summary>
/// Interface for hooks that execute after a tool is invoked.
/// </summary>
public interface IPostInvokeHook
{
    /// <summary>
    /// Gets the priority of this hook. Lower values execute first.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Called after a tool is invoked. Can modify the result.
    /// </summary>
    /// <param name="context">The hook context containing request information.</param>
    /// <param name="result">The result from the tool invocation.</param>
    /// <returns>The potentially modified result.</returns>
    ValueTask<CallToolResult> OnPostInvokeAsync(HookContext<CallToolRequestParams> context, CallToolResult result);
}

/// <summary>
/// Combined interface for hooks that need both pre and post invocation handling.
/// </summary>
public interface IToolHook : IPreInvokeHook, IPostInvokeHook
{
}
