using System.Net.Http.Headers;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;

namespace McpProxy.Core.Authentication;

/// <summary>
/// A delegating handler that adds Azure AD bearer tokens to outbound HTTP requests.
/// </summary>
public sealed class AzureAdAuthorizationHandler : DelegatingHandler
{
    private readonly AzureAdCredentialProvider _credentialProvider;
    private readonly ILogger _logger;
    private readonly Func<string?>? _userTokenAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdAuthorizationHandler"/>.
    /// </summary>
    /// <param name="credentialProvider">The credential provider to use for acquiring tokens.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="userTokenAccessor">
    /// Optional function to get the current user's token for on-behalf-of flow.
    /// When using OBO, this should return the user's access token from the inbound request.
    /// </param>
    public AzureAdAuthorizationHandler(
        AzureAdCredentialProvider credentialProvider,
        ILogger logger,
        Func<string?>? userTokenAccessor = null)
        : base(new HttpClientHandler())
    {
        _credentialProvider = credentialProvider;
        _logger = logger;
        _userTokenAccessor = userTokenAccessor;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdAuthorizationHandler"/> with an inner handler.
    /// </summary>
    /// <param name="credentialProvider">The credential provider to use for acquiring tokens.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="innerHandler">The inner handler to delegate to.</param>
    /// <param name="userTokenAccessor">
    /// Optional function to get the current user's token for on-behalf-of flow.
    /// </param>
    public AzureAdAuthorizationHandler(
        AzureAdCredentialProvider credentialProvider,
        ILogger logger,
        HttpMessageHandler innerHandler,
        Func<string?>? userTokenAccessor = null)
        : base(innerHandler)
    {
        _credentialProvider = credentialProvider;
        _logger = logger;
        _userTokenAccessor = userTokenAccessor;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get user token for OBO flow if available
            var userToken = _userTokenAccessor?.Invoke();

            // Acquire the access token
            var accessToken = await _credentialProvider.AcquireTokenAsync(userToken, cancellationToken)
                .ConfigureAwait(false);

            // Set the Authorization header
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        catch (Exception ex)
        {
            ProxyLogger.BackendAuthorizationFailed(_logger, ex);
            throw;
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _credentialProvider.Dispose();
        }

        base.Dispose(disposing);
    }
}
