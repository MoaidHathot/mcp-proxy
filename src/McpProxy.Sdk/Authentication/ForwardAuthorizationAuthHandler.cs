using McpProxy.Abstractions;
using Microsoft.AspNetCore.Http;

namespace McpProxy.Sdk.Authentication;

/// <summary>
/// Inbound authentication handler for the ForwardAuthorization mode.
/// This handler requires that a Bearer token is present on incoming requests
/// but does NOT validate the token cryptographically. The actual token validation
/// is performed by the backend server. If no token is present, the handler
/// returns a 401 challenge with RFC 9728 OAuth Protected Resource Metadata hints
/// so the MCP client (e.g., VS Code) can trigger its OAuth flow.
/// </summary>
/// <remarks>
/// This is the inbound counterpart to <see cref="ForwardAuthorizationHandler"/>
/// which is an outbound <see cref="System.Net.Http.DelegatingHandler"/> that copies
/// the <c>Authorization</c> header from the incoming HTTP context to outgoing
/// backend requests. Together they implement transparent auth forwarding:
/// <list type="number">
///   <item>This handler gates the proxy — no token means 401 with discovery hints.</item>
///   <item><see cref="ForwardAuthorizationHandler"/> copies the token to backend calls.</item>
/// </list>
/// </remarks>
public sealed class ForwardAuthorizationAuthHandler : IAuthenticationHandler
{
    private readonly IOAuthMetadataRegistry? _oauthRegistry;

    /// <summary>
    /// Initializes a new instance of <see cref="ForwardAuthorizationAuthHandler"/>.
    /// </summary>
    /// <param name="oauthRegistry">
    /// Optional OAuth metadata registry. When provided, the 401 challenge includes
    /// a <c>resource_metadata</c> parameter pointing the client to the RFC 9728
    /// protected resource metadata endpoint where it can discover the authorization
    /// server and required scopes.
    /// </param>
    public ForwardAuthorizationAuthHandler(IOAuthMetadataRegistry? oauthRegistry = null)
    {
        _oauthRegistry = oauthRegistry;
    }

    /// <inheritdoc />
    public string SchemeName => "Bearer";

    /// <inheritdoc />
    public ValueTask<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Check for the Authorization header
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Authorization header not provided"));
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Invalid authorization scheme. Expected Bearer token."));
        }

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Bearer token is empty"));
        }

        // Token is present — accept it without cryptographic validation.
        // The backend will validate it when the request is forwarded.
        return ValueTask.FromResult(AuthenticationResult.Success("forward-auth-user"));
    }

    /// <inheritdoc />
    public ValueTask ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Build the WWW-Authenticate challenge header.
        // Per RFC 9728, we include a resource_metadata parameter that tells the client
        // where to find the OAuth Protected Resource Metadata document. This allows
        // VS Code (and other MCP clients) to discover the authorization server and
        // required scopes automatically.
        var challenge = "Bearer";

        var probeResult = _oauthRegistry?.GetPrimaryProbeResult();
        if (probeResult is not null && probeResult.SupportsOAuthProtectedResource)
        {
            // Construct the resource_metadata URL pointing to our proxy's own metadata endpoint.
            // The OAuthMetadataProxyMiddleware serves these at /.well-known/oauth-protected-resource/...
            var proxyBaseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

            // The MCP endpoint path — use the request path to determine where the client
            // was trying to connect, or fall back to /mcp
            var mcpPath = context.Request.Path.HasValue
                ? context.Request.Path.Value
                : "/mcp";

            var resourceMetadataUrl = $"{proxyBaseUrl}/.well-known/oauth-protected-resource{mcpPath}";
            challenge = $"Bearer resource_metadata=\"{resourceMetadataUrl}\"";
        }

        context.Response.Headers.Append("WWW-Authenticate", challenge);
        return ValueTask.CompletedTask;
    }
}
