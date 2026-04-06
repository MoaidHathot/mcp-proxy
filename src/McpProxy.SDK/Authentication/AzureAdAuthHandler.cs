using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace McpProxy.SDK.Authentication;

/// <summary>
/// Authentication handler that validates Azure AD (Microsoft Entra ID) tokens.
/// Automatically fetches and caches JWKS from the Azure AD OpenID Connect metadata endpoint.
/// </summary>
public sealed class AzureAdAuthHandler : IAuthenticationHandler
{
    private readonly AzureAdConfiguration _config;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdAuthHandler"/>.
    /// </summary>
    /// <param name="config">The Azure AD configuration.</param>
    public AzureAdAuthHandler(AzureAdConfiguration config)
    {
        _config = config;
        _tokenHandler = new JwtSecurityTokenHandler();

        // Set up automatic OpenID Connect configuration retrieval
        var metadataAddress = $"{config.Authority}/.well-known/openid-configuration";
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdAuthHandler"/> with a custom configuration manager.
    /// Used for testing purposes.
    /// </summary>
    /// <param name="config">The Azure AD configuration.</param>
    /// <param name="configManager">The configuration manager for OpenID Connect.</param>
    internal AzureAdAuthHandler(AzureAdConfiguration config, ConfigurationManager<OpenIdConnectConfiguration> configManager)
    {
        _config = config;
        _tokenHandler = new JwtSecurityTokenHandler();
        _configManager = configManager;
    }

    /// <inheritdoc />
    public string SchemeName => "Bearer";

    /// <inheritdoc />
    public async ValueTask<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Get the Authorization header
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return AuthenticationResult.Failure("Authorization header not provided");
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationResult.Failure("Invalid authorization scheme. Expected 'Bearer'");
        }

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticationResult.Failure("Bearer token not provided");
        }

        try
        {
            // Get the latest OpenID Connect configuration (includes JWKS)
            var openIdConfig = await _configManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            
            var validationParameters = CreateValidationParameters(openIdConfig);
            var principal = await ValidateTokenAsync(token, validationParameters, cancellationToken).ConfigureAwait(false);

            // Validate required scopes if configured
            if (_config.RequiredScopes is { Length: > 0 })
            {
                if (!HasRequiredScope(principal, _config.RequiredScopes))
                {
                    return AuthenticationResult.Failure("Token does not contain required scope");
                }
            }

            // Validate required roles if configured
            if (_config.RequiredRoles is { Length: > 0 })
            {
                if (!HasRequiredRole(principal, _config.RequiredRoles))
                {
                    return AuthenticationResult.Failure("Token does not contain required role");
                }
            }

            var principalId = GetPrincipalId(principal);
            var properties = ExtractProperties(principal);

            return AuthenticationResult.Success(principalId, properties);
        }
        catch (SecurityTokenExpiredException)
        {
            return AuthenticationResult.Failure("Token has expired");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return AuthenticationResult.Failure("Invalid token signature");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return AuthenticationResult.Failure("Invalid token audience");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return AuthenticationResult.Failure("Invalid token issuer");
        }
        catch (SecurityTokenException ex)
        {
            return AuthenticationResult.Failure($"Token validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return AuthenticationResult.Failure($"Authentication failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public ValueTask ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var challengeValue = $"Bearer realm=\"{_config.Authority}\"";
        
        // Add error description for OAuth 2.0 compliance
        if (context.Items.TryGetValue("McpProxy.Authentication.FailureReason", out var reason) && reason is string reasonStr)
        {
            challengeValue += $", error=\"invalid_token\", error_description=\"{reasonStr}\"";
        }

        context.Response.Headers.Append("WWW-Authenticate", challengeValue);
        return ValueTask.CompletedTask;
    }

    private TokenValidationParameters CreateValidationParameters(OpenIdConnectConfiguration openIdConfig)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = _config.ValidateIssuer,
            ValidateAudience = _config.ValidateAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = openIdConfig.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        // Set audience validation
        if (_config.ValidateAudience)
        {
            parameters.ValidAudience = _config.Audience ?? _config.ClientId;
        }

        // Set issuer validation
        if (_config.ValidateIssuer)
        {
            if (_config.ValidIssuers is { Length: > 0 })
            {
                parameters.ValidIssuers = _config.ValidIssuers;
            }
            else
            {
                // Default Azure AD issuers for the tenant
                parameters.ValidIssuers = GetDefaultValidIssuers();
            }
        }

        return parameters;
    }

    private string[] GetDefaultValidIssuers()
    {
        var tenantId = _config.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return [];
        }

        // Azure AD v1 and v2 issuer formats
        return
        [
            $"https://login.microsoftonline.com/{tenantId}/",
            $"https://login.microsoftonline.com/{tenantId}/v2.0",
            $"https://sts.windows.net/{tenantId}/"
        ];
    }

    private ValueTask<ClaimsPrincipal> ValidateTokenAsync(string token, TokenValidationParameters parameters, CancellationToken cancellationToken)
    {
        // Note: JwtSecurityTokenHandler.ValidateToken is synchronous, but we keep the async signature
        // for potential future use with async token validation providers
        var principal = _tokenHandler.ValidateToken(token, parameters, out _);
        return ValueTask.FromResult(principal);
    }

    private static bool HasRequiredScope(ClaimsPrincipal principal, string[] requiredScopes)
    {
        var scopeClaims = principal.FindAll("scp")
            .Concat(principal.FindAll("scope"))
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredScopes.Any(scope => scopeClaims.Contains(scope));
    }

    private static bool HasRequiredRole(ClaimsPrincipal principal, string[] requiredRoles)
    {
        var roleClaims = principal.FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("roles"))
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredRoles.Any(role => roleClaims.Contains(role));
    }

    private static string GetPrincipalId(ClaimsPrincipal principal)
    {
        // Try various claim types that Azure AD uses for user/app identity
        return principal.FindFirstValue("oid")  // Object ID (most reliable)
            ?? principal.FindFirstValue("sub")   // Subject
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("azp")   // Authorized Party (for app tokens)
            ?? principal.FindFirstValue("appid") // Application ID (v1 tokens)
            ?? principal.Identity?.Name
            ?? "authenticated-user";
    }

    private static Dictionary<string, string> ExtractProperties(ClaimsPrincipal principal)
    {
        var properties = new Dictionary<string, string>();

        // Extract common Azure AD claims
        var claimsToExtract = new[]
        {
            ("oid", "objectId"),
            ("tid", "tenantId"),
            ("azp", "authorizedParty"),
            ("appid", "applicationId"),
            ("name", "name"),
            ("preferred_username", "preferredUsername"),
            ("email", "email"),
            ("upn", "userPrincipalName")
        };

        foreach (var (claimType, propertyName) in claimsToExtract)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrEmpty(value))
            {
                properties[propertyName] = value;
            }
        }

        // Extract roles
        var roles = principal.FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("roles"))
            .Select(c => c.Value)
            .ToList();

        if (roles.Count > 0)
        {
            properties["roles"] = string.Join(",", roles);
        }

        // Extract scopes
        var scopes = principal.FindAll("scp")
            .Concat(principal.FindAll("scope"))
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        if (scopes.Count > 0)
        {
            properties["scopes"] = string.Join(",", scopes);
        }

        return properties;
    }
}
