namespace McpProxy.Samples.TeamsIntegration;

/// <summary>
/// Configuration options for Teams integration.
/// </summary>
public sealed class TeamsIntegrationOptions
{
    /// <summary>
    /// Gets or sets the path to the cache file.
    /// Default is %LOCALAPPDATA%/mcp-proxy/teams-cache.json on Windows,
    /// ~/.local/share/mcp-proxy/teams-cache.json on Linux/macOS.
    /// </summary>
    public string? CacheFilePath { get; set; }

    /// <summary>
    /// Gets or sets the cache time-to-live.
    /// Default is 4 hours.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Gets or sets the maximum number of recent contacts to keep.
    /// Default is 50.
    /// </summary>
    public int MaxRecentContacts { get; set; } = 50;

    /// <summary>
    /// Gets or sets the default pagination limit for list operations.
    /// Default is 20.
    /// </summary>
    public int DefaultPaginationLimit { get; set; } = 20;

    /// <summary>
    /// Gets or sets whether to enable credential scanning on outbound messages.
    /// Default is true.
    /// </summary>
    public bool EnableCredentialScanning { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to block messages with detected credentials.
    /// If false, only warns. Default is true.
    /// </summary>
    public bool BlockCredentials { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable message prefixing.
    /// Default is false.
    /// </summary>
    public bool EnableMessagePrefix { get; set; } = false;

    /// <summary>
    /// Gets or sets the message prefix to add.
    /// Default is "[AI]".
    /// </summary>
    public string MessagePrefix { get; set; } = "[AI]";

    /// <summary>
    /// Gets or sets whether to enable automatic cache population from MCP responses.
    /// Default is true.
    /// </summary>
    public bool EnableCachePopulation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to auto-save the cache after updates.
    /// Default is false (manual save required).
    /// </summary>
    public bool AutoSaveCache { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable cache short-circuiting.
    /// When enabled, fresh cached data is returned instead of calling MCP.
    /// Default is true.
    /// </summary>
    public bool EnableCacheShortCircuit { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable automatic pagination.
    /// Default is true.
    /// </summary>
    public bool EnableAutoPagination { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to register virtual tools.
    /// Default is true.
    /// </summary>
    public bool RegisterVirtualTools { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to load the cache from disk on startup.
    /// Default is true.
    /// </summary>
    public bool LoadCacheOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the Teams MCP server to intercept.
    /// Set to null to intercept all servers. Default is null.
    /// </summary>
    public string? TeamsServerName { get; set; }
}
