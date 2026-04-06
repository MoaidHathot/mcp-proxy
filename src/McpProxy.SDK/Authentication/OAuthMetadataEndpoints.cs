using McpProxy.SDK.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Serialization;

namespace McpProxy.SDK.Authentication;

/// <summary>
/// Extension methods for mapping OAuth 2.0 metadata discovery endpoints.
/// Implements RFC 8414 OAuth 2.0 Authorization Server Metadata for MCP authentication.
/// </summary>
public static class OAuthMetadataEndpointExtensions
{
    /// <summary>
    /// Maps the OAuth 2.0 authorization server metadata endpoint.
    /// This endpoint allows MCP clients to discover how to authenticate with this server.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="authConfig">The authentication configuration.</param>
    /// <param name="baseUrl">The base URL of this server (used for building absolute URLs).</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOAuthMetadata(
        this IEndpointRouteBuilder endpoints,
        AuthenticationConfiguration authConfig,
        string? baseUrl = null)
    {
        // Only expose OAuth metadata if authentication is enabled
        if (!authConfig.Enabled || authConfig.Type == AuthenticationType.None)
        {
            return endpoints;
        }

        // RFC 8414: OAuth 2.0 Authorization Server Metadata
        endpoints.MapGet("/.well-known/oauth-authorization-server", (HttpContext context) =>
        {
            var serverBaseUrl = baseUrl ?? GetServerBaseUrl(context);
            var metadata = CreateMetadata(authConfig, serverBaseUrl);
            
            context.Response.ContentType = "application/json";
            return Results.Json(metadata, OAuthMetadataJsonContext.Default.OAuthServerMetadata);
        });

        // Alternative path that some clients may use
        endpoints.MapGet("/.well-known/openid-configuration", (HttpContext context) =>
        {
            var serverBaseUrl = baseUrl ?? GetServerBaseUrl(context);
            var metadata = CreateMetadata(authConfig, serverBaseUrl);
            
            context.Response.ContentType = "application/json";
            return Results.Json(metadata, OAuthMetadataJsonContext.Default.OAuthServerMetadata);
        });

        return endpoints;
    }

    private static OAuthServerMetadata CreateMetadata(AuthenticationConfiguration authConfig, string serverBaseUrl)
    {
        // When using Azure AD, delegate to Azure AD endpoints
        if (authConfig.Type == AuthenticationType.AzureAd)
        {
            return CreateAzureAdMetadata(authConfig.AzureAd, serverBaseUrl);
        }

        // For Bearer token auth with external authority
        if (authConfig.Type == AuthenticationType.Bearer && !string.IsNullOrEmpty(authConfig.Bearer.Authority))
        {
            return CreateBearerMetadata(authConfig.Bearer, serverBaseUrl);
        }

        // For API key auth, return minimal metadata pointing to local token endpoint
        return CreateApiKeyMetadata(serverBaseUrl);
    }

    private static OAuthServerMetadata CreateAzureAdMetadata(AzureAdConfiguration azureAd, string serverBaseUrl)
    {
        var authority = azureAd.Authority;
        var tenantId = azureAd.TenantId ?? "common";

        return new OAuthServerMetadata
        {
            Issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
            AuthorizationEndpoint = $"{authority}/oauth2/v2.0/authorize",
            TokenEndpoint = $"{authority}/oauth2/v2.0/token",
            JwksUri = $"https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys",
            RegistrationEndpoint = null, // Azure AD uses app registration in the portal
            ScopesSupported = ["openid", "profile", "email", "offline_access"],
            ResponseTypesSupported = ["code", "token", "id_token", "code id_token"],
            ResponseModesSupported = ["query", "fragment", "form_post"],
            GrantTypesSupported = ["authorization_code", "client_credentials", "refresh_token"],
            TokenEndpointAuthMethodsSupported = ["client_secret_basic", "client_secret_post", "private_key_jwt"],
            SubjectTypesSupported = ["pairwise"],
            IdTokenSigningAlgValuesSupported = ["RS256"],
            CodeChallengeMethodsSupported = ["S256"], // PKCE support (required by MCP)
            McpServerUrl = serverBaseUrl,
            McpProtocolVersion = "2025-03-26"
        };
    }

    private static OAuthServerMetadata CreateBearerMetadata(BearerConfiguration bearer, string serverBaseUrl)
    {
        var authority = bearer.Authority!.TrimEnd('/');

        return new OAuthServerMetadata
        {
            Issuer = authority,
            AuthorizationEndpoint = $"{authority}/authorize",
            TokenEndpoint = $"{authority}/token",
            JwksUri = $"{authority}/.well-known/jwks.json",
            RegistrationEndpoint = $"{authority}/register",
            ScopesSupported = ["openid", "profile"],
            ResponseTypesSupported = ["code", "token"],
            ResponseModesSupported = ["query", "fragment"],
            GrantTypesSupported = ["authorization_code", "client_credentials"],
            TokenEndpointAuthMethodsSupported = ["client_secret_basic", "client_secret_post"],
            SubjectTypesSupported = ["public"],
            IdTokenSigningAlgValuesSupported = ["RS256"],
            CodeChallengeMethodsSupported = ["S256"],
            McpServerUrl = serverBaseUrl,
            McpProtocolVersion = "2025-03-26"
        };
    }

    private static OAuthServerMetadata CreateApiKeyMetadata(string serverBaseUrl)
    {
        // For API key auth, we indicate that this server uses a custom auth method
        return new OAuthServerMetadata
        {
            Issuer = serverBaseUrl,
            AuthorizationEndpoint = null, // API key doesn't use OAuth flow
            TokenEndpoint = null,
            JwksUri = null,
            RegistrationEndpoint = null,
            ScopesSupported = [],
            ResponseTypesSupported = [],
            ResponseModesSupported = [],
            GrantTypesSupported = [],
            TokenEndpointAuthMethodsSupported = ["api_key"],
            SubjectTypesSupported = [],
            IdTokenSigningAlgValuesSupported = [],
            CodeChallengeMethodsSupported = [],
            McpServerUrl = serverBaseUrl,
            McpProtocolVersion = "2025-03-26",
            McpAuthMethod = "api_key",
            McpApiKeyHeader = "X-API-Key"
        };
    }

    private static string GetServerBaseUrl(HttpContext context)
    {
        var request = context.Request;
        return $"{request.Scheme}://{request.Host}";
    }
}

/// <summary>
/// OAuth 2.0 Authorization Server Metadata as defined in RFC 8414.
/// Extended with MCP-specific fields.
/// </summary>
public sealed class OAuthServerMetadata
{
    /// <summary>
    /// The authorization server's issuer identifier (URL).
    /// </summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>
    /// URL of the authorization endpoint.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// URL of the token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// URL of the JSON Web Key Set document.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JwksUri { get; set; }

    /// <summary>
    /// URL of the dynamic client registration endpoint.
    /// </summary>
    [JsonPropertyName("registration_endpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationEndpoint { get; set; }

    /// <summary>
    /// List of supported OAuth 2.0 scope values.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ScopesSupported { get; set; }

    /// <summary>
    /// List of supported OAuth 2.0 response types.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ResponseTypesSupported { get; set; }

    /// <summary>
    /// List of supported OAuth 2.0 response modes.
    /// </summary>
    [JsonPropertyName("response_modes_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ResponseModesSupported { get; set; }

    /// <summary>
    /// List of supported OAuth 2.0 grant types.
    /// </summary>
    [JsonPropertyName("grant_types_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? GrantTypesSupported { get; set; }

    /// <summary>
    /// List of supported client authentication methods for the token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TokenEndpointAuthMethodsSupported { get; set; }

    /// <summary>
    /// List of supported subject types.
    /// </summary>
    [JsonPropertyName("subject_types_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? SubjectTypesSupported { get; set; }

    /// <summary>
    /// List of supported signing algorithms for ID tokens.
    /// </summary>
    [JsonPropertyName("id_token_signing_alg_values_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? IdTokenSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// List of supported PKCE code challenge methods.
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? CodeChallengeMethodsSupported { get; set; }

    /// <summary>
    /// MCP extension: URL of the MCP server.
    /// </summary>
    [JsonPropertyName("mcp_server_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpServerUrl { get; set; }

    /// <summary>
    /// MCP extension: Protocol version supported by this MCP server.
    /// </summary>
    [JsonPropertyName("mcp_protocol_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpProtocolVersion { get; set; }

    /// <summary>
    /// MCP extension: Custom authentication method (for non-OAuth auth).
    /// </summary>
    [JsonPropertyName("mcp_auth_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpAuthMethod { get; set; }

    /// <summary>
    /// MCP extension: Header name for API key authentication.
    /// </summary>
    [JsonPropertyName("mcp_api_key_header")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpApiKeyHeader { get; set; }
}

/// <summary>
/// Source-generated JSON serialization context for OAuth metadata.
/// </summary>
[JsonSerializable(typeof(OAuthServerMetadata))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class OAuthMetadataJsonContext : JsonSerializerContext
{
}
