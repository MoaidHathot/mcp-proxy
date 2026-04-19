using McpProxy.Abstractions;
using System.Text.Json.Serialization;

namespace McpProxy.Sdk.Configuration;

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

    /// <summary>
    /// Gets or sets the caching configuration.
    /// </summary>
    public CachingConfiguration Caching { get; set; } = new();

    /// <summary>
    /// Gets or sets the capability configuration.
    /// </summary>
    public CapabilityConfiguration Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets the telemetry configuration.
    /// </summary>
    public TelemetryConfiguration Telemetry { get; set; } = new();

    /// <summary>
    /// Gets or sets the debug configuration.
    /// </summary>
    public DebugConfiguration Debug { get; set; } = new();

    /// <summary>
    /// Gets or sets the global CORS configuration. Applied as the default policy
    /// to all MCP routes; individual servers may override via their own
    /// <see cref="ServerConfiguration.Cors"/> property.
    /// </summary>
    public CorsConfiguration Cors { get; set; } = new();
}

/// <summary>
/// Cross-Origin Resource Sharing (CORS) configuration for MCP HTTP endpoints.
/// </summary>
/// <remarks>
/// When <see cref="Enabled"/> is <c>true</c>, ASP.NET Core CORS middleware is
/// registered and applied to MCP routes. The <c>Mcp-Session-Id</c> header is
/// automatically appended to <see cref="ExposedHeaders"/> so browser-based MCP
/// clients can read the session id returned by the initialize response.
/// </remarks>
public sealed class CorsConfiguration
{
    /// <summary>
    /// Gets or sets whether CORS is enabled. When <c>false</c>, no CORS headers
    /// are emitted and preflight (OPTIONS) requests are not handled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the allowed origins. Use <c>["*"]</c> to allow any origin
    /// (incompatible with <see cref="AllowCredentials"/>). When <c>null</c> or
    /// empty, no origins are allowed.
    /// <para>
    /// Entries may include <c>*</c> as a glob wildcard for any character
    /// sequence within a single URI segment, e.g. <c>http://localhost:*</c>
    /// or <c>https://*.example.com</c>. When any entry contains a wildcard
    /// (other than the special <c>"*"</c> allow-all value), origin matching
    /// is performed via a predicate rather than exact string comparison.
    /// </para>
    /// </summary>
    public string[]? AllowedOrigins { get; set; }

    /// <summary>
    /// Gets or sets whether to allow any localhost origin on any port over
    /// either HTTP or HTTPS. When <c>true</c>, requests from <c>localhost</c>
    /// or <c>127.0.0.1</c> on any port are accepted in addition to anything
    /// listed in <see cref="AllowedOrigins"/>. Convenient for local
    /// development tools (e.g., MCP Inspector) that bind to ephemeral ports.
    /// </summary>
    public bool AllowAnyLocalhost { get; set; }

    /// <summary>
    /// Gets or sets the allowed HTTP methods. When <c>null</c> or empty, any
    /// method is allowed.
    /// </summary>
    public string[]? AllowedMethods { get; set; }

    /// <summary>
    /// Gets or sets the allowed request headers. When <c>null</c> or empty, any
    /// header is allowed.
    /// </summary>
    public string[]? AllowedHeaders { get; set; }

    /// <summary>
    /// Gets or sets the response headers exposed to browser scripts. The
    /// <c>Mcp-Session-Id</c> header is always exposed, regardless of this list.
    /// </summary>
    public string[]? ExposedHeaders { get; set; }

    /// <summary>
    /// Gets or sets whether credentials (cookies, authorization headers) are
    /// allowed. Cannot be combined with wildcard origins.
    /// </summary>
    public bool AllowCredentials { get; set; }

    /// <summary>
    /// Gets or sets the max age (in seconds) browsers may cache preflight
    /// responses. <c>null</c> uses the browser default.
    /// </summary>
    public int? PreflightMaxAgeSeconds { get; set; }
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
    Bearer,

    /// <summary>
    /// Azure Active Directory (Microsoft Entra ID) authentication.
    /// </summary>
    AzureAd,

    /// <summary>
    /// Forward authorization mode. The proxy requires a Bearer token to be present
    /// on incoming requests but does not validate it cryptographically. The token is
    /// forwarded as-is to the backend server which performs the actual validation.
    /// If no token is present, the proxy returns a 401 challenge so the MCP client
    /// (e.g., VS Code) can trigger its OAuth flow.
    /// </summary>
    ForwardAuthorization
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

    /// <summary>
    /// Gets or sets the Azure AD (Microsoft Entra ID) configuration.
    /// </summary>
    public AzureAdConfiguration AzureAd { get; set; } = new();
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
/// Azure Active Directory (Microsoft Entra ID) authentication configuration.
/// </summary>
public sealed class AzureAdConfiguration
{
    /// <summary>
    /// Gets or sets the Azure AD instance URL.
    /// Default is "https://login.microsoftonline.com/".
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Gets or sets the Azure AD tenant ID.
    /// Can be the tenant GUID or domain name (e.g., "contoso.onmicrosoft.com").
    /// Use "common" for multi-tenant applications.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the application (client) ID registered in Azure AD.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the expected audience for token validation.
    /// If not specified, defaults to the ClientId.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets the valid issuers for token validation.
    /// If not specified, defaults to the Azure AD issuer based on TenantId.
    /// </summary>
    public string[]? ValidIssuers { get; set; }

    /// <summary>
    /// Gets or sets whether to validate the issuer.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate the audience.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Gets or sets the required scopes for authorization.
    /// If specified, the token must contain at least one of these scopes.
    /// </summary>
    public string[]? RequiredScopes { get; set; }

    /// <summary>
    /// Gets or sets the required roles for authorization.
    /// If specified, the token must contain at least one of these roles.
    /// </summary>
    public string[]? RequiredRoles { get; set; }

    /// <summary>
    /// Gets the authority URL constructed from Instance and TenantId.
    /// </summary>
    public string Authority => TenantId is not null 
        ? $"{Instance.TrimEnd('/')}/{TenantId}" 
        : Instance.TrimEnd('/');
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

/// <summary>
/// Caching configuration.
/// </summary>
public sealed class CachingConfiguration
{
    /// <summary>
    /// Gets or sets the tool cache configuration.
    /// </summary>
    public ToolCacheSettings Tools { get; set; } = new();
}

/// <summary>
/// Tool cache settings.
/// </summary>
public sealed class ToolCacheSettings
{
    /// <summary>
    /// Gets or sets whether tool caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the time-to-live for cached tool lists in seconds.
    /// Default is 60 seconds.
    /// </summary>
    public int TtlSeconds { get; set; } = 60;
}

/// <summary>
/// Capability configuration for the proxy.
/// </summary>
public sealed class CapabilityConfiguration
{
    /// <summary>
    /// Gets or sets the server capabilities to advertise to clients.
    /// </summary>
    public ServerCapabilitySettings Server { get; set; } = new();

    /// <summary>
    /// Gets or sets the client capabilities to advertise to backend servers.
    /// </summary>
    public ClientCapabilitySettings Client { get; set; } = new();
}

/// <summary>
/// Server capability settings advertised to clients.
/// </summary>
public sealed class ServerCapabilitySettings
{
    /// <summary>
    /// Gets or sets experimental/custom capabilities.
    /// These are non-standard capabilities that the proxy advertises to clients.
    /// Each key represents a capability name, and the value is the capability configuration object.
    /// </summary>
    public Dictionary<string, object>? Experimental { get; set; }
}

/// <summary>
/// Client capability settings advertised to backend servers.
/// </summary>
public sealed class ClientCapabilitySettings
{
    /// <summary>
    /// Gets or sets whether sampling capability is enabled.
    /// When enabled, backend servers can request LLM completions from the client.
    /// </summary>
    public bool Sampling { get; set; } = true;

    /// <summary>
    /// Gets or sets whether elicitation capability is enabled.
    /// When enabled, backend servers can request user input.
    /// </summary>
    public bool Elicitation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether roots capability is enabled.
    /// When enabled, backend servers can request file system roots.
    /// </summary>
    public bool Roots { get; set; } = true;

    /// <summary>
    /// Gets or sets experimental/custom capabilities.
    /// These are non-standard capabilities that the proxy advertises to backend servers.
    /// Each key represents a capability name, and the value is the capability configuration object.
    /// </summary>
    public Dictionary<string, object>? Experimental { get; set; }
}

/// <summary>
/// Telemetry configuration for OpenTelemetry integration.
/// </summary>
public sealed class TelemetryConfiguration
{
    /// <summary>
    /// Gets or sets whether telemetry is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the service name for telemetry.
    /// </summary>
    public string ServiceName { get; set; } = "McpProxy";

    /// <summary>
    /// Gets or sets the service version for telemetry.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Gets or sets the metrics configuration.
    /// </summary>
    public MetricsConfiguration Metrics { get; set; } = new();

    /// <summary>
    /// Gets or sets the tracing configuration.
    /// </summary>
    public TracingConfiguration Tracing { get; set; } = new();
}

/// <summary>
/// Metrics configuration for OpenTelemetry.
/// </summary>
public sealed class MetricsConfiguration
{
    /// <summary>
    /// Gets or sets whether metrics are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the console exporter (for debugging).
    /// </summary>
    public bool ConsoleExporter { get; set; } = false;

    /// <summary>
    /// Gets or sets the OTLP endpoint for metrics export.
    /// </summary>
    public string? OtlpEndpoint { get; set; }
}

/// <summary>
/// Tracing configuration for OpenTelemetry.
/// </summary>
public sealed class TracingConfiguration
{
    /// <summary>
    /// Gets or sets whether tracing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the console exporter (for debugging).
    /// </summary>
    public bool ConsoleExporter { get; set; } = false;

    /// <summary>
    /// Gets or sets the OTLP endpoint for trace export.
    /// </summary>
    public string? OtlpEndpoint { get; set; }
}

/// <summary>
/// Debug configuration for development and troubleshooting.
/// </summary>
public sealed class DebugConfiguration
{
    /// <summary>
    /// Gets or sets whether hook execution tracing is enabled.
    /// When enabled, detailed timing and execution information is logged for each hook.
    /// </summary>
    public bool HookTracing { get; set; }

    /// <summary>
    /// Gets or sets whether to include timing information in hook traces.
    /// </summary>
    public bool IncludeHookTiming { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the health endpoint is enabled.
    /// The health endpoint is localhost-only for security.
    /// </summary>
    public bool HealthEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the path for the health endpoint.
    /// </summary>
    public string HealthEndpointPath { get; set; } = "/debug/health";

    /// <summary>
    /// Gets or sets the dump configuration for request/response dumping.
    /// </summary>
    public DumpConfiguration Dump { get; set; } = new();
}

/// <summary>
/// Configuration for request/response dumping.
/// </summary>
public sealed class DumpConfiguration
{
    /// <summary>
    /// Gets or sets whether request/response dumping is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the output directory for dump files.
    /// If null, dumps are written to the console.
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Gets or sets whether to pretty-print JSON output.
    /// </summary>
    public bool PrettyPrint { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum payload size in KB.
    /// Larger payloads are truncated with a marker.
    /// </summary>
    public int MaxPayloadSizeKb { get; set; } = 1024;

    /// <summary>
    /// Gets or sets server names to include in dumps.
    /// If null or empty, all servers are included.
    /// </summary>
    public string[]? ServerFilter { get; set; }

    /// <summary>
    /// Gets or sets tool names to include in dumps.
    /// If null or empty, all tools are included.
    /// </summary>
    public string[]? ToolFilter { get; set; }
}
