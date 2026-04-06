using McpProxy.Sdk.Configuration;

namespace McpProxy.Sdk.Proxy;

/// <summary>
/// Contains information about a connected MCP backend client.
/// </summary>
public sealed class McpClientInfo
{
    /// <summary>
    /// Gets the unique name/identifier for this server.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the configuration for this server.
    /// </summary>
    public required ServerConfiguration Configuration { get; init; }

    /// <summary>
    /// Gets the MCP client wrapper instance.
    /// </summary>
    public required IMcpClientWrapper Client { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client is connected.
    /// </summary>
    public bool IsConnected => Client is not null;
}
