using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace McpProxy.Core.Authentication;

/// <summary>
/// Authentication handler that validates Bearer tokens (JWT).
/// </summary>
public sealed class BearerTokenAuthHandler : IAuthenticationHandler
{
    private readonly BearerConfiguration _config;
    private readonly TokenValidationParameters _validationParameters;

    /// <summary>
    /// Initializes a new instance of <see cref="BearerTokenAuthHandler"/>.
    /// </summary>
    /// <param name="config">The bearer token configuration.</param>
    public BearerTokenAuthHandler(BearerConfiguration config)
    {
        _config = config;
        _validationParameters = CreateValidationParameters(config);
    }

    /// <inheritdoc />
    public string SchemeName => "Bearer";

    /// <inheritdoc />
    public ValueTask<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Get the Authorization header
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Authorization header not provided"));
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Invalid authorization scheme"));
        }

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Bearer token not provided"));
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParameters, out _);

            var principalId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub")
                ?? principal.Identity?.Name
                ?? "authenticated-user";

            var properties = new Dictionary<string, string>();

            // Extract useful claims
            foreach (var claim in principal.Claims)
            {
                if (claim.Type is ClaimTypes.Role or "roles" or "scope")
                {
                    properties[$"claim:{claim.Type}"] = claim.Value;
                }
            }

            return ValueTask.FromResult(AuthenticationResult.Success(principalId, properties));
        }
        catch (SecurityTokenExpiredException)
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Token has expired"));
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return ValueTask.FromResult(AuthenticationResult.Failure("Invalid token signature"));
        }
        catch (SecurityTokenException ex)
        {
            return ValueTask.FromResult(AuthenticationResult.Failure($"Token validation failed: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public ValueTask ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var challengeValue = "Bearer";
        if (!string.IsNullOrEmpty(_config.Authority))
        {
            challengeValue += $" realm=\"{_config.Authority}\"";
        }

        context.Response.Headers.Append("WWW-Authenticate", challengeValue);
        return ValueTask.CompletedTask;
    }

    private static TokenValidationParameters CreateValidationParameters(BearerConfiguration config)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = config.ValidateIssuer,
            ValidateAudience = config.ValidateAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };

        if (!string.IsNullOrEmpty(config.Audience))
        {
            parameters.ValidAudience = config.Audience;
        }

        if (config.ValidIssuers is { Length: > 0 })
        {
            parameters.ValidIssuers = config.ValidIssuers;
        }
        else if (!string.IsNullOrEmpty(config.Authority))
        {
            parameters.ValidIssuer = config.Authority;
        }

        // Note: In a real implementation, you would configure the IssuerSigningKey
        // from the authority's JWKS endpoint. For simplicity, this relies on
        // ASP.NET Core's JwtBearer middleware when used in the full pipeline.
        // This handler is primarily for custom scenarios.

        return parameters;
    }
}
