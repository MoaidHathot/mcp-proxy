using System.Text.Json.Serialization;

namespace McpProxy.Abstractions;

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
    /// </summary>
    ForwardAuthorization,

    /// <summary>
    /// Azure identity using <c>DefaultAzureCredential</c> from <c>Azure.Identity</c>.
    /// </summary>
    AzureDefaultCredential,

    /// <summary>
    /// Interactive browser authentication using a public client ID.
    /// </summary>
    InteractiveBrowser
}
