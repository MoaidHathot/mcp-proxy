using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace McpProxy.Core.Authentication;

/// <summary>
/// Probes backend servers to detect OAuth metadata endpoints.
/// </summary>
public interface IOAuthMetadataProbe
{
    /// <summary>
    /// Probes a backend URL to detect which OAuth metadata endpoints it supports.
    /// </summary>
    /// <param name="backendUrl">The base URL of the backend server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The probe result indicating which endpoints are supported.</returns>
    Task<OAuthProbeResult> ProbeAsync(string backendUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of probing a backend for OAuth metadata endpoints.
/// </summary>
public sealed class OAuthProbeResult
{
    /// <summary>
    /// The backend URL that was probed.
    /// </summary>
    public required string BackendUrl { get; init; }

    /// <summary>
    /// Whether the backend supports /.well-known/oauth-authorization-server.
    /// </summary>
    public bool SupportsOAuthAuthorizationServer { get; init; }

    /// <summary>
    /// Whether the backend supports /.well-known/openid-configuration.
    /// </summary>
    public bool SupportsOpenIdConfiguration { get; init; }

    /// <summary>
    /// The cached OAuth authorization server metadata (if supported).
    /// </summary>
    public string? OAuthAuthorizationServerMetadata { get; init; }

    /// <summary>
    /// The cached OpenID configuration metadata (if supported).
    /// </summary>
    public string? OpenIdConfigurationMetadata { get; init; }

    /// <summary>
    /// Whether any OAuth metadata endpoints are supported.
    /// </summary>
    public bool SupportsOAuth => SupportsOAuthAuthorizationServer || SupportsOpenIdConfiguration;

    /// <summary>
    /// Creates a result indicating no OAuth support.
    /// </summary>
    public static OAuthProbeResult NoSupport(string backendUrl) => new()
    {
        BackendUrl = backendUrl,
        SupportsOAuthAuthorizationServer = false,
        SupportsOpenIdConfiguration = false
    };
}

/// <summary>
/// Default implementation of <see cref="IOAuthMetadataProbe"/>.
/// </summary>
public sealed class OAuthMetadataProbe : IOAuthMetadataProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthMetadataProbe> _logger;
    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Initializes a new instance of <see cref="OAuthMetadataProbe"/>.
    /// </summary>
    public OAuthMetadataProbe(IHttpClientFactory httpClientFactory, ILogger<OAuthMetadataProbe> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OAuthProbeResult> ProbeAsync(string backendUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendUrl);

        var baseUrl = backendUrl.TrimEnd('/');
        ProxyLogger.OAuthMetadataProbeStarting(_logger, baseUrl);

        // Probe both endpoints in parallel
        var oauthTask = ProbeEndpointAsync(baseUrl, "/.well-known/oauth-authorization-server", cancellationToken);
        var openIdTask = ProbeEndpointAsync(baseUrl, "/.well-known/openid-configuration", cancellationToken);

        await Task.WhenAll(oauthTask, openIdTask).ConfigureAwait(false);

        var oauthMetadata = await oauthTask.ConfigureAwait(false);
        var openIdMetadata = await openIdTask.ConfigureAwait(false);

        var result = new OAuthProbeResult
        {
            BackendUrl = baseUrl,
            SupportsOAuthAuthorizationServer = oauthMetadata is not null,
            SupportsOpenIdConfiguration = openIdMetadata is not null,
            OAuthAuthorizationServerMetadata = oauthMetadata,
            OpenIdConfigurationMetadata = openIdMetadata
        };

        if (result.SupportsOAuth)
        {
            ProxyLogger.OAuthMetadataProbeSuccess(_logger, baseUrl,
                result.SupportsOAuthAuthorizationServer, result.SupportsOpenIdConfiguration);
        }
        else
        {
            ProxyLogger.OAuthMetadataProbeNoSupport(_logger, baseUrl);
        }

        return result;
    }

    private async Task<string?> ProbeEndpointAsync(string baseUrl, string path, CancellationToken cancellationToken)
    {
        var targetUrl = $"{baseUrl}{path}";

        try
        {
            using var client = _httpClientFactory.CreateClient("OAuthMetadataProbe");
            client.Timeout = s_probeTimeout;

            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                ProxyLogger.OAuthMetadataProbeEndpointFound(_logger, path, targetUrl);
                return content;
            }

            ProxyLogger.OAuthMetadataProbeEndpointNotFound(_logger, path, targetUrl, (int)response.StatusCode);
            return null;
        }
        catch (HttpRequestException ex)
        {
            ProxyLogger.OAuthMetadataProbeEndpointError(_logger, path, targetUrl, ex);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ProxyLogger.OAuthMetadataProbeEndpointTimeout(_logger, path, targetUrl);
            return null;
        }
    }
}
