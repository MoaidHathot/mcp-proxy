using McpProxy.Sdk.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpProxy.Sdk.Authentication;

/// <summary>
/// Middleware that serves OAuth metadata proxied from backend servers.
/// This middleware automatically handles:
/// <list type="bullet">
/// <item><c>/.well-known/oauth-authorization-server</c> (RFC 8414)</item>
/// <item><c>/.well-known/openid-configuration</c> (OpenID Connect Discovery)</item>
/// <item><c>/.well-known/oauth-protected-resource/...</c> (RFC 9728)</item>
/// </list>
/// requests for backends that support OAuth.
/// </summary>
public sealed class OAuthMetadataProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OAuthMetadataProxyMiddleware> _logger;
    private readonly ConcurrentDictionary<string, CachedMetadata> _cache = new();
    private readonly TimeSpan _cacheDuration;

    private const string ProtectedResourcePrefix = "/.well-known/oauth-protected-resource";

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

        if (path is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Check if this is an RFC 9728 OAuth Protected Resource Metadata request
        if (path.StartsWith(ProtectedResourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (await TryServeProtectedResourceMetadataAsync(context, registry, path).ConfigureAwait(false))
            {
                return;
            }
        }

        // Check if this is a standard OAuth metadata request (RFC 8414 / OIDC)
        if (IsOAuthPath(path))
        {
            var backendUrl = registry.GetPrimaryOAuthBackendUrl();
            if (backendUrl is not null)
            {
                await ServeOAuthMetadataAsync(context, backendUrl, path).ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to serve RFC 9728 OAuth Protected Resource Metadata.
    /// When VS Code connects to our proxy (e.g., at /mcp), it requests
    /// /.well-known/oauth-protected-resource/mcp. This method intercepts that
    /// and returns the protected resource metadata from the backend.
    /// </summary>
    private async Task<bool> TryServeProtectedResourceMetadataAsync(
        HttpContext context,
        IOAuthMetadataRegistry registry,
        string path)
    {
        var probeResult = registry.GetPrimaryProbeResult();
        if (probeResult is null || !probeResult.SupportsOAuthProtectedResource || probeResult.OAuthProtectedResourceMetadata is null)
        {
            return false;
        }

        ProxyLogger.ServingProtectedResourceMetadata(_logger, path);

        var cacheKey = $"protected-resource:{path}";

        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Cache"] = "HIT";
            await context.Response.WriteAsync(cached.Content, context.RequestAborted).ConfigureAwait(false);
            return true;
        }

        // Rewrite the resource field in the metadata to point to our proxy instead of the backend.
        // The client sees our proxy as the resource, so the `resource` field should be our URL.
        var proxyBaseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var resourcePath = path[ProtectedResourcePrefix.Length..]; // e.g., "/mcp"
        var proxyResourceUrl = $"{proxyBaseUrl}{resourcePath}";

        var metadata = RewriteProtectedResourceMetadata(
            probeResult.OAuthProtectedResourceMetadata,
            proxyResourceUrl);

        _cache[cacheKey] = new CachedMetadata
        {
            Content = metadata,
            ContentType = "application/json",
            StatusCode = 200,
            ExpiresAt = DateTime.UtcNow.Add(_cacheDuration)
        };

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Cache"] = "MISS";
        await context.Response.WriteAsync(metadata, context.RequestAborted).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Rewrites the <c>resource</c> field in the protected resource metadata to point to the proxy.
    /// This is necessary because the client sees our proxy as the resource, not the backend.
    /// </summary>
    private static string RewriteProtectedResourceMetadata(string originalMetadata, string proxyResourceUrl)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(originalMetadata);
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();

                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("resource"))
                    {
                        // Replace the resource URL with our proxy URL
                        writer.WriteString("resource", proxyResourceUrl);
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (System.Text.Json.JsonException)
        {
            // If we can't parse/rewrite, return the original
            return originalMetadata;
        }
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

    /// <summary>
    /// Gets the probe result for the primary OAuth backend (if any).
    /// </summary>
    /// <returns>The probe result, or null if no OAuth backends are registered.</returns>
    OAuthProbeResult? GetPrimaryProbeResult();
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
    private OAuthProbeResult? _primaryProbeResult;
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
            if (_primaryBackendUrl is null)
            {
                _primaryBackendUrl = backendUrl;
                _primaryProbeResult = probeResult;
            }
        }
    }

    /// <inheritdoc />
    public string? GetPrimaryOAuthBackendUrl()
    {
        return _primaryBackendUrl;
    }

    /// <inheritdoc />
    public OAuthProbeResult? GetPrimaryProbeResult()
    {
        return _primaryProbeResult;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, OAuthBackendInfo> GetAllBackends()
    {
        return _backends;
    }
}
