using System.Text.Json.Serialization;

namespace McpProxy.Core.Configuration;

/// <summary>
/// Backend server transport type.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServerTransportType>))]
public enum ServerTransportType
{
    /// <summary>
    /// Standard input/output transport (local process).
    /// </summary>
    Stdio,

    /// <summary>
    /// HTTP transport with auto-detection (Streamable HTTP or SSE fallback).
    /// </summary>
    Http,

    /// <summary>
    /// Server-Sent Events transport only.
    /// </summary>
    Sse
}

/// <summary>
/// Configuration for a backend MCP server.
/// </summary>
public sealed class ServerConfiguration
{
    /// <summary>
    /// Gets or sets the transport type.
    /// </summary>
    public ServerTransportType Type { get; set; }

    /// <summary>
    /// Gets or sets the display title for this server.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description of this server.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the command to execute (for STDIO transport).
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Gets or sets the command arguments (for STDIO transport).
    /// </summary>
    public string[]? Arguments { get; set; }

    /// <summary>
    /// Gets or sets the URL of the MCP server (for HTTP/SSE transport).
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets custom headers to send with HTTP requests (for HTTP/SSE transport).
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the route path for this server (when using PerServer routing mode).
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Gets or sets the environment variables to set when launching the server process.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Gets or sets the tools configuration for this server.
    /// </summary>
    public ToolsConfiguration Tools { get; set; } = new();

    /// <summary>
    /// Gets or sets the resources configuration for this server.
    /// </summary>
    public ResourcesConfiguration Resources { get; set; } = new();

    /// <summary>
    /// Gets or sets the prompts configuration for this server.
    /// </summary>
    public PromptsConfiguration Prompts { get; set; } = new();

    /// <summary>
    /// Gets or sets the hooks configuration for this server.
    /// </summary>
    public HooksConfiguration Hooks { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Tools filtering and transformation configuration.
/// </summary>
public sealed class ToolsConfiguration
{
    /// <summary>
    /// Gets or sets the prefix to add to all tool names from this server.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets the separator between prefix and tool name.
    /// </summary>
    public string PrefixSeparator { get; set; } = "_";

    /// <summary>
    /// Gets or sets the filter configuration.
    /// </summary>
    public FilterConfiguration Filter { get; set; } = new();
}

/// <summary>
/// Resources filtering and transformation configuration.
/// </summary>
public sealed class ResourcesConfiguration
{
    /// <summary>
    /// Gets or sets the prefix to add to all resource URIs from this server.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets the separator between prefix and resource URI.
    /// </summary>
    public string PrefixSeparator { get; set; } = "://";

    /// <summary>
    /// Gets or sets the filter configuration.
    /// </summary>
    public FilterConfiguration Filter { get; set; } = new();
}

/// <summary>
/// Prompts filtering and transformation configuration.
/// </summary>
public sealed class PromptsConfiguration
{
    /// <summary>
    /// Gets or sets the prefix to add to all prompt names from this server.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets the separator between prefix and prompt name.
    /// </summary>
    public string PrefixSeparator { get; set; } = "_";

    /// <summary>
    /// Gets or sets the filter configuration.
    /// </summary>
    public FilterConfiguration Filter { get; set; } = new();
}

/// <summary>
/// Filter mode for tools.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FilterMode>))]
public enum FilterMode
{
    /// <summary>
    /// No filtering - include all tools.
    /// </summary>
    None,

    /// <summary>
    /// AllowList mode - only include tools matching patterns.
    /// </summary>
    AllowList,

    /// <summary>
    /// DenyList mode - exclude tools matching patterns.
    /// </summary>
    DenyList,

    /// <summary>
    /// Regex mode - include/exclude based on regex patterns.
    /// </summary>
    Regex
}

/// <summary>
/// Filter configuration for tools.
/// </summary>
public sealed class FilterConfiguration
{
    /// <summary>
    /// Gets or sets the filter mode.
    /// </summary>
    public FilterMode Mode { get; set; } = FilterMode.None;

    /// <summary>
    /// Gets or sets the patterns for filtering. Interpretation depends on mode:
    /// - AllowList: Tool names or wildcard patterns to include
    /// - DenyList: Tool names or wildcard patterns to exclude
    /// - Regex: Regex patterns (first pattern is include, optional second is exclude)
    /// </summary>
    public string[]? Patterns { get; set; }

    /// <summary>
    /// Gets or sets whether pattern matching is case-insensitive.
    /// </summary>
    public bool CaseInsensitive { get; set; } = true;
}

/// <summary>
/// Hooks configuration for a server.
/// </summary>
public sealed class HooksConfiguration
{
    /// <summary>
    /// Gets or sets the pre-invoke hooks to execute before tool calls.
    /// </summary>
    public HookDefinition[]? PreInvoke { get; set; }

    /// <summary>
    /// Gets or sets the post-invoke hooks to execute after tool calls.
    /// </summary>
    public HookDefinition[]? PostInvoke { get; set; }
}

/// <summary>
/// Definition of a hook from configuration.
/// </summary>
public sealed class HookDefinition
{
    /// <summary>
    /// Gets or sets the hook type (e.g., "logging", "inputTransform", "outputTransform").
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets the hook configuration as key-value pairs.
    /// </summary>
    public Dictionary<string, object?>? Config { get; set; }
}
