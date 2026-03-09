using McpProxy.Core.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace McpProxy.Core.Authentication;

/// <summary>
/// Extension methods for proxying OAuth 2.0 metadata discovery endpoints from a backend server.
/// This enables pass-through authentication where the proxy forwards OAuth metadata from the
/// backend MCP server, allowing clients like VS Code to authenticate directly with the backend's
/// identity provider.
/// </summary>
public static class OAuthMetadataProxyEndpointExtensions
{
    private static readonly string[] s_oAuthMetadataPaths =
    [
        "/.well-known/oauth-authorization-server",
        "/.well-known/openid-configuration"
    ];

    /// <summary>
    /// Maps OAuth metadata endpoints that proxy requests to a backend server.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="backendUrl">The base URL of the backend MCP server.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory. If not provided, creates a default client.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapProxiedOAuthMetadata(
        this IEndpointRouteBuilder endpoints,
        string backendUrl,
        IHttpClientFactory? httpClientFactory = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendUrl);

        var backendBaseUrl = backendUrl.TrimEnd('/');

        foreach (var path in s_oAuthMetadataPaths)
        {
            endpoints.MapGet(path, async (HttpContext context) =>
            {
                var targetUrl = $"{backendBaseUrl}{path}";

                try
                {
                    using var httpClient = httpClientFactory?.CreateClient("OAuthMetadataProxy")
                        ?? new HttpClient();

                    // Set a reasonable timeout for metadata fetches
                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    // Create the request
                    using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    // Forward any authorization header (in case backend requires auth for metadata)
                    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", authHeader.ToString());
                    }

                    // Send the request
                    using var response = await httpClient.SendAsync(request, context.RequestAborted).ConfigureAwait(false);

                    // Copy status code
                    context.Response.StatusCode = (int)response.StatusCode;

                    // Copy relevant headers
                    if (response.Content.Headers.ContentType is not null)
                    {
                        context.Response.ContentType = response.Content.Headers.ContentType.ToString();
                    }

                    // Copy the response body
                    var content = await response.Content.ReadAsStringAsync(context.RequestAborted).ConfigureAwait(false);
                    await context.Response.WriteAsync(content, context.RequestAborted).ConfigureAwait(false);

                    if (logger is not null)
                    {
                        ProxyLogger.OAuthMetadataProxied(logger, targetUrl, response.StatusCode);
                    }
                }
                catch (HttpRequestException ex)
                {
                    if (logger is not null)
                    {
                        ProxyLogger.OAuthMetadataFetchFailed(logger, targetUrl, ex);
                    }

                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        $"{{\"error\": \"Failed to fetch OAuth metadata from backend\", \"target\": \"{targetUrl}\"}}",
                        context.RequestAborted).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    if (logger is not null)
                    {
                        ProxyLogger.OAuthMetadataTimeout(logger, targetUrl);
                    }

                    context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        $"{{\"error\": \"Timeout fetching OAuth metadata from backend\", \"target\": \"{targetUrl}\"}}",
                        context.RequestAborted).ConfigureAwait(false);
                }
            });
        }

        if (logger is not null)
        {
            ProxyLogger.OAuthMetadataProxyMapped(logger, backendBaseUrl);
        }

        return endpoints;
    }

    /// <summary>
    /// Maps OAuth metadata endpoints that proxy requests to a backend server,
    /// with caching to reduce load on the backend.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="backendUrl">The base URL of the backend MCP server.</param>
    /// <param name="cacheDuration">How long to cache the metadata. Default is 5 minutes.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapCachedProxiedOAuthMetadata(
        this IEndpointRouteBuilder endpoints,
        string backendUrl,
        TimeSpan? cacheDuration = null,
        IHttpClientFactory? httpClientFactory = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendUrl);

        var backendBaseUrl = backendUrl.TrimEnd('/');
        var cacheExpiry = cacheDuration ?? TimeSpan.FromMinutes(5);
        var cache = new Dictionary<string, CachedMetadata>();
        var cacheLock = new object();

        foreach (var path in s_oAuthMetadataPaths)
        {
            endpoints.MapGet(path, async (HttpContext context) =>
            {
                var targetUrl = $"{backendBaseUrl}{path}";

                // Check cache
                lock (cacheLock)
                {
                    if (cache.TryGetValue(path, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
                    {
                        context.Response.StatusCode = cached.StatusCode;
                        context.Response.ContentType = cached.ContentType;
                        context.Response.Headers["X-Cache"] = "HIT";
                        return Results.Content(cached.Content, cached.ContentType, statusCode: cached.StatusCode);
                    }
                }

                try
                {
                    using var httpClient = httpClientFactory?.CreateClient("OAuthMetadataProxy")
                        ?? new HttpClient();

                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
                        lock (cacheLock)
                        {
                            cache[path] = new CachedMetadata
                            {
                                Content = content,
                                ContentType = contentType,
                                StatusCode = statusCode,
                                ExpiresAt = DateTime.UtcNow.Add(cacheExpiry)
                            };
                        }
                    }

                    context.Response.Headers["X-Cache"] = "MISS";
                    return Results.Content(content, contentType, statusCode: statusCode);
                }
                catch (HttpRequestException ex)
                {
                    if (logger is not null)
                    {
                        ProxyLogger.OAuthMetadataFetchFailed(logger, targetUrl, ex);
                    }

                    return Results.Json(
                        new { error = "Failed to fetch OAuth metadata from backend", target = targetUrl },
                        statusCode: StatusCodes.Status502BadGateway);
                }
                catch (TaskCanceledException)
                {
                    if (logger is not null)
                    {
                        ProxyLogger.OAuthMetadataTimeout(logger, targetUrl);
                    }

                    return Results.Json(
                        new { error = "Timeout fetching OAuth metadata from backend", target = targetUrl },
                        statusCode: StatusCodes.Status504GatewayTimeout);
                }
            });
        }

        if (logger is not null)
        {
            ProxyLogger.OAuthMetadataProxyCachedMapped(logger, backendBaseUrl, cacheExpiry);
        }

        return endpoints;
    }

    private sealed class CachedMetadata
    {
        public required string Content { get; init; }
        public required string ContentType { get; init; }
        public required int StatusCode { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }
}
