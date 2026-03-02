using System.Text.Json.Serialization;

namespace McpProxy.Core.Configuration;

/// <summary>
/// Root configuration for the MCP Proxy.
/// </summary>
public sealed class ProxyConfiguration
{
    /// <summary>
    /// Gets or sets the proxy-level settings.
    /// </summary>
    public ProxySettings Proxy { get; set; } = new();

    /// <summary>
    /// Gets or sets the MCP server configurations keyed by server name.
    /// </summary>
    public Dictionary<string, ServerConfiguration> Mcp { get; set; } = [];
}

/// <summary>
/// Proxy-level settings.
/// </summary>
public sealed class ProxySettings
{
    /// <summary>
    /// Gets or sets the server info to expose to clients.
    /// </summary>
    public ServerInfo ServerInfo { get; set; } = new();

    /// <summary>
    /// Gets or sets the routing configuration.
    /// </summary>
    public RoutingConfiguration Routing { get; set; } = new();

    /// <summary>
    /// Gets or sets the authentication configuration.
    /// </summary>
    public AuthenticationConfiguration Authentication { get; set; } = new();

    /// <summary>
    /// Gets or sets the logging configuration.
    /// </summary>
    public LoggingConfiguration Logging { get; set; } = new();
}

/// <summary>
/// Server information exposed to MCP clients.
/// </summary>
public sealed class ServerInfo
{
    /// <summary>
    /// Gets or sets the server name.
    /// </summary>
    public string Name { get; set; } = "MCP Proxy";

    /// <summary>
    /// Gets or sets the server version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the server instructions.
    /// </summary>
    public string? Instructions { get; set; }
}

/// <summary>
/// Routing mode for the proxy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RoutingMode>))]
public enum RoutingMode
{
    /// <summary>
    /// All servers are aggregated under a single endpoint.
    /// </summary>
    Unified,

    /// <summary>
    /// Each server is exposed on its own endpoint.
    /// </summary>
    PerServer
}

/// <summary>
/// Routing configuration.
/// </summary>
public sealed class RoutingConfiguration
{
    /// <summary>
    /// Gets or sets the routing mode.
    /// </summary>
    public RoutingMode Mode { get; set; } = RoutingMode.Unified;

    /// <summary>
    /// Gets or sets the base path for MCP endpoints.
    /// </summary>
    public string BasePath { get; set; } = "/mcp";
}

/// <summary>
/// Authentication type.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuthenticationType>))]
public enum AuthenticationType
{
    /// <summary>
    /// No authentication required.
    /// </summary>
    None,

    /// <summary>
    /// API key authentication.
    /// </summary>
    ApiKey,

    /// <summary>
    /// Bearer token (JWT) authentication.
    /// </summary>
    Bearer
}

/// <summary>
/// Authentication configuration.
/// </summary>
public sealed class AuthenticationConfiguration
{
    /// <summary>
    /// Gets or sets whether authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the authentication type.
    /// </summary>
    public AuthenticationType Type { get; set; } = AuthenticationType.None;

    /// <summary>
    /// Gets or sets the API key configuration.
    /// </summary>
    public ApiKeyConfiguration ApiKey { get; set; } = new();

    /// <summary>
    /// Gets or sets the Bearer token configuration.
    /// </summary>
    public BearerConfiguration Bearer { get; set; } = new();
}

/// <summary>
/// API key authentication configuration.
/// </summary>
public sealed class ApiKeyConfiguration
{
    /// <summary>
    /// Gets or sets the header name for the API key.
    /// </summary>
    public string Header { get; set; } = "X-API-Key";

    /// <summary>
    /// Gets or sets the query parameter name for the API key (optional fallback).
    /// </summary>
    public string? QueryParameter { get; set; }

    /// <summary>
    /// Gets or sets the expected API key value. Supports "env:VARIABLE_NAME" syntax.
    /// </summary>
    public string? Value { get; set; }
}

/// <summary>
/// Bearer token (JWT) authentication configuration.
/// </summary>
public sealed class BearerConfiguration
{
    /// <summary>
    /// Gets or sets the authority URL for token validation.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Gets or sets the expected audience.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets whether to validate the issuer.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate the audience.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Gets or sets the valid issuers.
    /// </summary>
    public string[]? ValidIssuers { get; set; }
}

/// <summary>
/// Logging configuration.
/// </summary>
public sealed class LoggingConfiguration
{
    /// <summary>
    /// Gets or sets whether to log incoming requests.
    /// </summary>
    public bool LogRequests { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to log outgoing responses.
    /// </summary>
    public bool LogResponses { get; set; }

    /// <summary>
    /// Gets or sets whether to mask sensitive data in logs.
    /// </summary>
    public bool SensitiveDataMask { get; set; } = true;
}
