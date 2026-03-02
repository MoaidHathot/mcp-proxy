using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace McpProxy.Core.Authentication;

/// <summary>
/// Middleware that handles authentication for MCP proxy requests.
/// </summary>
public sealed class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuthenticationHandler? _authHandler;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly bool _enabled;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthenticationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="config">The authentication configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public AuthenticationMiddleware(
        RequestDelegate next,
        AuthenticationConfiguration config,
        ILogger<AuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _enabled = config.Enabled;

        if (_enabled)
        {
            _authHandler = config.Type switch
            {
                AuthenticationType.ApiKey => new ApiKeyAuthHandler(config.ApiKey),
                AuthenticationType.Bearer => new BearerTokenAuthHandler(config.Bearer),
                AuthenticationType.AzureAd => new AzureAdAuthHandler(config.AzureAd),
                _ => null
            };
        }
    }

    /// <summary>
    /// Processes an HTTP request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || _authHandler is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        ProxyLogger.Authenticating(_logger, _authHandler.SchemeName);

        var result = await _authHandler.AuthenticateAsync(context, context.RequestAborted).ConfigureAwait(false);

        if (!result.IsAuthenticated)
        {
            ProxyLogger.AuthenticationFailed(_logger, result.FailureReason ?? "Unknown reason");
            await _authHandler.ChallengeAsync(context, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        ProxyLogger.AuthenticationSucceeded(_logger, result.PrincipalId ?? "unknown");

        // Store authentication result in HttpContext for downstream use
        context.Items["McpProxy.Authentication.Result"] = result;
        context.Items["McpProxy.Authentication.PrincipalId"] = result.PrincipalId;

        await _next(context).ConfigureAwait(false);
    }
}

/// <summary>
/// Extension methods for authentication middleware.
/// </summary>
public static class AuthenticationMiddlewareExtensions
{
    /// <summary>
    /// Adds MCP Proxy authentication middleware to the pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="config">The authentication configuration.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseMcpProxyAuthentication(
        this IApplicationBuilder app,
        AuthenticationConfiguration config)
    {
        return app.UseMiddleware<AuthenticationMiddleware>(config);
    }
}
