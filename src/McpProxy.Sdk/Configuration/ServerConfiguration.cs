using System.Text.Json.Serialization;

namespace McpProxy.Sdk.Configuration;

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

    /// <summary>
    /// Gets or sets the authentication configuration for connecting to this backend server.
    /// Used for HTTP/SSE backends that require Azure AD or other OAuth authentication.
    /// </summary>
    public BackendAuthConfiguration? Auth { get; set; }
}

/// <summary>
/// Authentication type for backend server connections.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackendAuthType>))]
public enum BackendAuthType
{
    /// <summary>
    /// No authentication required.
    /// </summary>
    None,

    /// <summary>
    /// Azure AD authentication using client credentials (app-to-app).
    /// </summary>
    AzureAdClientCredentials,

    /// <summary>
    /// Azure AD authentication using on-behalf-of flow (user delegation).
    /// </summary>
    AzureAdOnBehalfOf,

    /// <summary>
    /// Azure AD authentication using managed identity.
    /// </summary>
    AzureAdManagedIdentity,

    /// <summary>
    /// Forward the incoming Authorization header from the client request to the backend.
    /// This allows the proxy to pass through authentication without needing its own credentials.
    /// Requires the proxy to be running in HTTP/SSE mode (not stdio).
    /// </summary>
    ForwardAuthorization,

    /// <summary>
    /// Azure identity using <c>DefaultAzureCredential</c> from <c>Azure.Identity</c>.
    /// Automatically discovers credentials from the local environment:
    /// Azure CLI (<c>az login</c>), environment variables, managed identity, Visual Studio, etc.
    /// Useful for local development scenarios where the developer is already authenticated.
    /// </summary>
    AzureDefaultCredential,

    /// <summary>
    /// Interactive browser authentication using a public client ID.
    /// Opens a browser for user sign-in on first use; subsequent requests use cached refresh tokens.
    /// Useful for authenticating as the current user against backends that require a pre-authorized
    /// public client ID (e.g., Microsoft 365 MCP servers via VS Code's app registration).
    /// Tokens are persisted to the OS credential store for silent re-authentication across restarts.
    /// </summary>
    InteractiveBrowser
}

/// <summary>
/// Authentication configuration for connecting to backend MCP servers.
/// </summary>
public sealed class BackendAuthConfiguration
{
    /// <summary>
    /// Gets or sets the authentication type.
    /// </summary>
    public BackendAuthType Type { get; set; } = BackendAuthType.None;

    /// <summary>
    /// Gets or sets the credential group name. Backends with the same credential group
    /// share a single credential instance, avoiding duplicate authentication prompts.
    /// Applies to <see cref="BackendAuthType.InteractiveBrowser"/> auth type.
    /// When not set, each backend gets its own credential instance.
    /// </summary>
    public string? CredentialGroup { get; set; }

    /// <summary>
    /// Gets or sets the Azure AD configuration for backend authentication.
    /// </summary>
    public BackendAzureAdConfiguration AzureAd { get; set; } = new();
}

/// <summary>
/// Azure AD configuration for outbound authentication to backend servers.
/// </summary>
public sealed class BackendAzureAdConfiguration
{
    /// <summary>
    /// Gets or sets the Azure AD instance URL.
    /// Default is "https://login.microsoftonline.com/".
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Gets or sets the Azure AD tenant ID.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the client (application) ID of this application.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for client credentials flow.
    /// Supports "env:VARIABLE_NAME" syntax to read from environment variables.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the path to the client certificate for certificate-based authentication.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the certificate thumbprint for certificate-based authentication
    /// (when loading from certificate store).
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Gets or sets the scopes to request when acquiring tokens.
    /// Example: ["api://backend-api/.default"] or ["https://graph.microsoft.com/.default"]
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// Gets or sets the managed identity client ID (for user-assigned managed identity).
    /// Leave null to use system-assigned managed identity.
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Gets or sets the redirect URI for interactive browser authentication.
    /// Default is <c>http://localhost</c>, which is the standard redirect for public client apps.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// Gets or sets the name for the persistent token cache used by interactive browser authentication.
    /// Tokens are persisted to the OS credential store (Windows Credential Manager, macOS Keychain, etc.)
    /// so that users only need to sign in once. Default is <c>"mcp-proxy"</c>.
    /// </summary>
    public string? TokenCacheName { get; set; }

    /// <summary>
    /// Gets or sets a pre-configured <see cref="Azure.Core.TokenCredential"/> to use for
    /// <see cref="BackendAuthType.AzureDefaultCredential"/> authentication.
    /// When set, this credential is used instead of creating a new <c>DefaultAzureCredential</c>.
    /// This enables scenarios like <c>InteractiveBrowserCredential</c> for user-delegated flows
    /// where the developer needs to consent to specific resource permissions.
    /// </summary>
    /// <remarks>
    /// This property is not serializable — it is intended for programmatic configuration only.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public Azure.Core.TokenCredential? TokenCredential { get; set; }

    /// <summary>
    /// Gets the authority URL constructed from Instance and TenantId.
    /// </summary>
    public string Authority => TenantId is not null
        ? $"{Instance.TrimEnd('/')}/{TenantId}"
        : Instance.TrimEnd('/');
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
