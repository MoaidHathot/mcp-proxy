using McpProxy.Core.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpProxy.Core.Authentication;

/// <summary>
/// Middleware that serves OAuth metadata proxied from backend servers.
/// This middleware automatically handles /.well-known/oauth-authorization-server and 
/// /.well-known/openid-configuration requests for backends that support OAuth.
/// </summary>
public sealed class OAuthMetadataProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OAuthMetadataProxyMiddleware> _logger;
    private readonly ConcurrentDictionary<string, CachedMetadata> _cache = new();
    private readonly TimeSpan _cacheDuration;

    private static readonly string[] s_oAuthPaths =
    [
        "/.well-known/oauth-authorization-server",
        "/.well-known/openid-configuration"
    ];

    /// <summary>
    /// Initializes a new instance of <see cref="OAuthMetadataProxyMiddleware"/>.
    /// </summary>
    public OAuthMetadataProxyMiddleware(
        RequestDelegate next,
        ILogger<OAuthMetadataProxyMiddleware> logger,
        TimeSpan? cacheDuration = null)
    {
        _next = next;
        _logger = logger;
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// Processes the HTTP request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, IOAuthMetadataRegistry registry)
    {
        var path = context.Request.Path.Value;

        // Check if this is an OAuth metadata request
        if (path is not null && IsOAuthPath(path))
        {
            // Check if we have a backend configured for OAuth
            var backendUrl = registry.GetPrimaryOAuthBackendUrl();
            if (backendUrl is not null)
            {
                await ServeOAuthMetadataAsync(context, backendUrl, path).ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool IsOAuthPath(string path)
    {
        foreach (var oauthPath in s_oAuthPaths)
        {
            if (path.Equals(oauthPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ServeOAuthMetadataAsync(HttpContext context, string backendUrl, string path)
    {
        var cacheKey = $"{backendUrl}{path}";

        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = cached.ContentType;
            context.Response.Headers["X-Cache"] = "HIT";
            await context.Response.WriteAsync(cached.Content, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        // Fetch from backend
        var targetUrl = $"{backendUrl.TrimEnd('/')}{path}";

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // Forward authorization header if present
            if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
            }

            using var response = await httpClient.SendAsync(request, context.RequestAborted).ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            var content = await response.Content.ReadAsStringAsync(context.RequestAborted).ConfigureAwait(false);

            // Cache successful responses
            if (response.IsSuccessStatusCode)
            {
                _cache[cacheKey] = new CachedMetadata
                {
                    Content = content,
                    ContentType = contentType,
                    StatusCode = statusCode,
                    ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
                };
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.Headers["X-Cache"] = "MISS";
            await context.Response.WriteAsync(content, context.RequestAborted).ConfigureAwait(false);

            ProxyLogger.OAuthMetadataProxied(_logger, targetUrl, response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            ProxyLogger.OAuthMetadataFetchFailed(_logger, targetUrl, ex);

            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                $"{{\"error\": \"Failed to fetch OAuth metadata from backend\", \"target\": \"{targetUrl}\"}}",
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            ProxyLogger.OAuthMetadataTimeout(_logger, targetUrl);

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                $"{{\"error\": \"Timeout fetching OAuth metadata from backend\", \"target\": \"{targetUrl}\"}}",
                context.RequestAborted).ConfigureAwait(false);
        }
    }

    private sealed class CachedMetadata
    {
        public required string Content { get; init; }
        public required string ContentType { get; init; }
        public required int StatusCode { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }
}

/// <summary>
/// Registry for OAuth metadata backend URLs.
/// This registry tracks which backends support OAuth and should have their metadata proxied.
/// </summary>
public interface IOAuthMetadataRegistry
{
    /// <summary>
    /// Registers a backend URL for OAuth metadata proxying.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="backendUrl">The backend URL.</param>
    /// <param name="probeResult">The probe result.</param>
    void Register(string serverName, string backendUrl, OAuthProbeResult probeResult);

    /// <summary>
    /// Gets the primary OAuth backend URL (the first one registered that supports OAuth).
    /// </summary>
    /// <returns>The backend URL, or null if no OAuth backends are registered.</returns>
    string? GetPrimaryOAuthBackendUrl();

    /// <summary>
    /// Gets all registered OAuth backends.
    /// </summary>
    IReadOnlyDictionary<string, OAuthBackendInfo> GetAllBackends();
}

/// <summary>
/// Information about a registered OAuth backend.
/// </summary>
public sealed class OAuthBackendInfo
{
    /// <summary>
    /// The server name.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// The backend URL.
    /// </summary>
    public required string BackendUrl { get; init; }

    /// <summary>
    /// The probe result.
    /// </summary>
    public required OAuthProbeResult ProbeResult { get; init; }
}

/// <summary>
/// Default implementation of <see cref="IOAuthMetadataRegistry"/>.
/// </summary>
public sealed class OAuthMetadataRegistry : IOAuthMetadataRegistry
{
    private readonly ConcurrentDictionary<string, OAuthBackendInfo> _backends = new();
    private string? _primaryBackendUrl;
    private readonly object _lock = new();

    /// <inheritdoc />
    public void Register(string serverName, string backendUrl, OAuthProbeResult probeResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(backendUrl);
        ArgumentNullException.ThrowIfNull(probeResult);

        if (!probeResult.SupportsOAuth)
        {
            return; // Don't register backends that don't support OAuth
        }

        var info = new OAuthBackendInfo
        {
            ServerName = serverName,
            BackendUrl = backendUrl,
            ProbeResult = probeResult
        };

        _backends[serverName] = info;

        lock (_lock)
        {
            // First backend to be registered becomes the primary
            _primaryBackendUrl ??= backendUrl;
        }
    }

    /// <inheritdoc />
    public string? GetPrimaryOAuthBackendUrl()
    {
        return _primaryBackendUrl;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, OAuthBackendInfo> GetAllBackends()
    {
        return _backends;
    }
}
