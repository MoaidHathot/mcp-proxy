using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Proxy;

/// <summary>
/// Wrapper around McpClient that implements IMcpClientWrapper for testability.
/// </summary>
public sealed class McpClientWrapper : IMcpClientWrapper
{
    private readonly McpClient _client;

    /// <summary>
    /// Initializes a new instance of <see cref="McpClientWrapper"/>.
    /// </summary>
    /// <param name="client">The underlying MCP client.</param>
    public McpClientWrapper(McpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<IList<Tool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var clientTools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return clientTools.Select(t => new Tool
        {
            Name = t.Name,
            Title = t.Title,
            Description = t.Description,
            InputSchema = t.JsonSchema,
            OutputSchema = t.ReturnJsonSchema
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        return await _client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IList<Resource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var clientResources = await _client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return clientResources.Select(r => new Resource
        {
            Uri = r.Uri,
            Name = r.Name,
            Description = r.Description,
            MimeType = r.MimeType
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<ReadResourceResult> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        return await _client.ReadResourceAsync(uri, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IList<Prompt>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        var clientPrompts = await _client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return clientPrompts.Select(p => new Prompt
        {
            Name = p.Name,
            Description = p.Description
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        return await _client.GetPromptAsync(name, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
    }
}
