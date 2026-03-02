using ModelContextProtocol.Protocol;

namespace McpProxy.Abstractions;

/// <summary>
/// Information about a cached tool and its source server.
/// </summary>
public sealed class CachedToolInfo
{
    /// <summary>
    /// Gets the tool definition.
    /// </summary>
    public required Tool Tool { get; init; }

    /// <summary>
    /// Gets the original (unprefixed) tool name.
    /// </summary>
    public required string OriginalName { get; init; }

    /// <summary>
    /// Gets the name of the server providing this tool.
    /// </summary>
    public required string ServerName { get; init; }
}

/// <summary>
/// Cache for tool listings with TTL-based expiration.
/// </summary>
public interface IToolCache
{
    /// <summary>
    /// Gets a tool by name from the cache.
    /// </summary>
    /// <param name="toolName">The tool name (may be prefixed).</param>
    /// <returns>The cached tool info, or null if not found or expired.</returns>
    CachedToolInfo? GetTool(string toolName);

    /// <summary>
    /// Gets all cached tools for a server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>The cached tools, or null if not cached or expired.</returns>
    IList<Tool>? GetToolsForServer(string serverName);

    /// <summary>
    /// Sets the tools for a server in the cache.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="tools">The tools to cache.</param>
    /// <param name="prefixedToolNames">Map of prefixed tool names to original names.</param>
    void SetToolsForServer(string serverName, IList<Tool> tools, IReadOnlyDictionary<string, string>? prefixedToolNames = null);

    /// <summary>
    /// Invalidates the cache for a specific server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    void InvalidateServer(string serverName);

    /// <summary>
    /// Invalidates all cached data.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Gets whether the cache has valid data for a server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>True if cache has valid data for the server.</returns>
    bool HasValidCache(string serverName);
}
