using System.Collections.Concurrent;
using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.SDK.Caching;

/// <summary>
/// Configuration for tool caching.
/// </summary>
public sealed class ToolCacheConfiguration
{
    /// <summary>
    /// Gets or sets whether caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the time-to-live for cached tool lists in seconds.
    /// Default is 60 seconds.
    /// </summary>
    public int TtlSeconds { get; set; } = 60;
}

/// <summary>
/// Cached entry for a server's tool list.
/// </summary>
internal sealed class ServerToolCacheEntry
{
    public required IList<Tool> Tools { get; init; }
    public required Dictionary<string, CachedToolInfo> ToolsByName { get; init; }
    public required Dictionary<string, CachedToolInfo> ToolsByPrefixedName { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string ServerName { get; init; }
}

/// <summary>
/// Thread-safe tool cache implementation with TTL-based expiration.
/// </summary>
public sealed class ToolCache : IToolCache
{
    private readonly ConcurrentDictionary<string, ServerToolCacheEntry> _serverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedToolInfo> _toolLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;
    private readonly ITimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="ToolCache"/> with the specified TTL.
    /// </summary>
    /// <param name="ttlSeconds">Time-to-live in seconds for cached entries.</param>
    public ToolCache(int ttlSeconds = 60)
        : this(ttlSeconds, SystemTimeProvider.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ToolCache"/> with the specified TTL and time provider.
    /// </summary>
    /// <param name="ttlSeconds">Time-to-live in seconds for cached entries.</param>
    /// <param name="timeProvider">Time provider for testing.</param>
    public ToolCache(int ttlSeconds, ITimeProvider timeProvider)
    {
        _ttl = TimeSpan.FromSeconds(ttlSeconds);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public CachedToolInfo? GetTool(string toolName)
    {
        // First check if any cached data is expired and clean up
        CleanupExpiredEntries();

        if (_toolLookup.TryGetValue(toolName, out var toolInfo))
        {
            // Verify the server cache is still valid
            if (_serverCache.TryGetValue(toolInfo.ServerName, out var entry) && !IsExpired(entry))
            {
                return toolInfo;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IList<Tool>? GetToolsForServer(string serverName)
    {
        if (_serverCache.TryGetValue(serverName, out var entry) && !IsExpired(entry))
        {
            return entry.Tools;
        }

        return null;
    }

    /// <inheritdoc />
    public void SetToolsForServer(string serverName, IList<Tool> tools, IReadOnlyDictionary<string, string>? prefixedToolNames = null)
    {
        var toolsByName = new Dictionary<string, CachedToolInfo>(StringComparer.OrdinalIgnoreCase);
        var toolsByPrefixedName = new Dictionary<string, CachedToolInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            var cachedInfo = new CachedToolInfo
            {
                Tool = tool,
                OriginalName = tool.Name,
                ServerName = serverName
            };

            toolsByName[tool.Name] = cachedInfo;
        }

        // Add prefixed name lookups if provided
        if (prefixedToolNames is not null)
        {
            foreach (var (prefixedName, originalName) in prefixedToolNames)
            {
                if (toolsByName.TryGetValue(originalName, out var cachedInfo))
                {
                    toolsByPrefixedName[prefixedName] = cachedInfo;
                }
            }
        }

        var entry = new ServerToolCacheEntry
        {
            Tools = tools,
            ToolsByName = toolsByName,
            ToolsByPrefixedName = toolsByPrefixedName,
            ExpiresAt = _timeProvider.UtcNow.Add(_ttl),
            ServerName = serverName
        };

        // Remove old entries from lookup before adding new ones
        if (_serverCache.TryGetValue(serverName, out var oldEntry))
        {
            foreach (var name in oldEntry.ToolsByName.Keys)
            {
                _toolLookup.TryRemove(name, out _);
            }
            foreach (var name in oldEntry.ToolsByPrefixedName.Keys)
            {
                _toolLookup.TryRemove(name, out _);
            }
        }

        _serverCache[serverName] = entry;

        // Add to global lookup
        foreach (var (name, info) in toolsByName)
        {
            _toolLookup[name] = info;
        }
        foreach (var (prefixedName, info) in toolsByPrefixedName)
        {
            _toolLookup[prefixedName] = info;
        }
    }

    /// <inheritdoc />
    public void InvalidateServer(string serverName)
    {
        if (_serverCache.TryRemove(serverName, out var entry))
        {
            foreach (var name in entry.ToolsByName.Keys)
            {
                _toolLookup.TryRemove(name, out _);
            }
            foreach (var name in entry.ToolsByPrefixedName.Keys)
            {
                _toolLookup.TryRemove(name, out _);
            }
        }
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        _serverCache.Clear();
        _toolLookup.Clear();
    }

    /// <inheritdoc />
    public bool HasValidCache(string serverName)
    {
        return _serverCache.TryGetValue(serverName, out var entry) && !IsExpired(entry);
    }

    private bool IsExpired(ServerToolCacheEntry entry)
    {
        return _timeProvider.UtcNow >= entry.ExpiresAt;
    }

    private void CleanupExpiredEntries()
    {
        var now = _timeProvider.UtcNow;
        var expiredServers = _serverCache
            .Where(kvp => now >= kvp.Value.ExpiresAt)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var serverName in expiredServers)
        {
            InvalidateServer(serverName);
        }
    }
}

/// <summary>
/// Abstraction for time to enable testing.
/// </summary>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current UTC time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// System time provider using the system clock.
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly SystemTimeProvider Instance = new();

    private SystemTimeProvider() { }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Fake time provider for testing.
/// </summary>
public sealed class FakeTimeProvider : ITimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _now;

    /// <summary>
    /// Sets the current time.
    /// </summary>
    public void SetTime(DateTimeOffset time) => _now = time;

    /// <summary>
    /// Advances the current time by the specified duration.
    /// </summary>
    public void Advance(TimeSpan duration) => _now = _now.Add(duration);
}

/// <summary>
/// Null cache that does not cache anything.
/// </summary>
public sealed class NullToolCache : IToolCache
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NullToolCache Instance = new();

    private NullToolCache() { }

    /// <inheritdoc />
    public CachedToolInfo? GetTool(string toolName) => null;

    /// <inheritdoc />
    public IList<Tool>? GetToolsForServer(string serverName) => null;

    /// <inheritdoc />
    public void SetToolsForServer(string serverName, IList<Tool> tools, IReadOnlyDictionary<string, string>? prefixedToolNames = null) { }

    /// <inheritdoc />
    public void InvalidateServer(string serverName) { }

    /// <inheritdoc />
    public void InvalidateAll() { }

    /// <inheritdoc />
    public bool HasValidCache(string serverName) => false;
}
