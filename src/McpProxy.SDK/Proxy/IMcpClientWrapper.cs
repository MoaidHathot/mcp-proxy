using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Proxy;

/// <summary>
/// Interface for MCP client operations, enabling testability.
/// </summary>
public interface IMcpClientWrapper : IAsyncDisposable
{
    /// <summary>
    /// Lists all tools available from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tools.</returns>
    Task<IList<Tool>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls a tool on the server.
    /// </summary>
    /// <param name="toolName">Name of the tool to call.</param>
    /// <param name="arguments">Arguments for the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the tool call.</returns>
    Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all resources available from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of resources.</returns>
    Task<IList<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">URI of the resource to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the resource content.</returns>
    Task<ReadResourceResult> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all prompts available from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of prompts.</returns>
    Task<IList<Prompt>> ListPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a prompt from the server.
    /// </summary>
    /// <param name="name">Name of the prompt.</param>
    /// <param name="arguments">Arguments for the prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the prompt.</returns>
    Task<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to updates for a resource.
    /// </summary>
    /// <param name="uri">URI of the resource to subscribe to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the subscription is established.</returns>
    Task SubscribeToResourceAsync(
        string uri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from updates for a resource.
    /// </summary>
    /// <param name="uri">URI of the resource to unsubscribe from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the unsubscription is processed.</returns>
    Task UnsubscribeFromResourceAsync(
        string uri,
        CancellationToken cancellationToken = default);
}
