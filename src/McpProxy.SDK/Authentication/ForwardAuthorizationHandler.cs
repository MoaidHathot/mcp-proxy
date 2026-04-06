using McpProxy.SDK.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace McpProxy.SDK.Authentication;

/// <summary>
/// A delegating handler that forwards the incoming Authorization header from the
/// client request to outbound requests to backend MCP servers.
/// </summary>
/// <remarks>
/// This handler enables pass-through authentication scenarios where VS Code (or another client)
/// authenticates to the proxy via HTTP, and the proxy forwards that authentication to the
/// backend MCP server without needing its own credentials.
/// 
/// This is useful when:
/// - The client handles OAuth/OIDC authentication directly
/// - The backend server accepts the same token that was sent to the proxy
/// - No token exchange (like OBO) is required
/// 
/// Note: This handler requires the proxy to run in HTTP/SSE mode, not stdio,
/// since stdio transport does not have HTTP headers.
/// </remarks>
public sealed class ForwardAuthorizationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ForwardAuthorizationHandler"/>.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor to retrieve the incoming request.</param>
    /// <param name="logger">The logger instance.</param>
    public ForwardAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger logger)
        : base(new HttpClientHandler())
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ForwardAuthorizationHandler"/> with an inner handler.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor to retrieve the incoming request.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="innerHandler">The inner handler to delegate to.</param>
    public ForwardAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger logger,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            ProxyLogger.ForwardAuthorizationNoContext(_logger);
            return base.SendAsync(request, cancellationToken);
        }

        if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authValue = authHeader.ToString();
            if (!string.IsNullOrEmpty(authValue))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authValue);
                ProxyLogger.ForwardAuthorizationHeaderAdded(_logger);
            }
            else
            {
                ProxyLogger.ForwardAuthorizationHeaderEmpty(_logger);
            }
        }
        else
        {
            ProxyLogger.ForwardAuthorizationHeaderMissing(_logger);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
