using Azure.Core;
using Azure.Identity;
using McpProxy.SDK.Logging;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace McpProxy.SDK.Authentication;

/// <summary>
/// A delegating handler that acquires bearer tokens using <see cref="DefaultAzureCredential"/>
/// and attaches them to outbound HTTP requests.
/// </summary>
/// <remarks>
/// <see cref="DefaultAzureCredential"/> automatically discovers credentials from the local
/// environment in the following order: environment variables, workload identity, managed identity,
/// Azure CLI, Azure PowerShell, Azure Developer CLI, Visual Studio, and Interactive Browser.
///
/// This is particularly useful for local development scenarios where the developer is already
/// authenticated via <c>az login</c>, and the proxy needs to acquire tokens for backend APIs
/// without requiring client secrets or certificates.
/// </remarks>
public sealed class DefaultAzureCredentialAuthHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultAzureCredentialAuthHandler"/>.
    /// </summary>
    /// <param name="scopes">The OAuth scopes to request when acquiring tokens.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="credential">
    /// Optional token credential to use. Defaults to <see cref="DefaultAzureCredential"/>.
    /// Useful for testing or when a specific credential chain is needed.
    /// </param>
    public DefaultAzureCredentialAuthHandler(
        string[] scopes,
        ILogger logger,
        TokenCredential? credential = null)
        : base(new HttpClientHandler())
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credential = credential ?? new DefaultAzureCredential();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultAzureCredentialAuthHandler"/> with an inner handler.
    /// </summary>
    /// <param name="scopes">The OAuth scopes to request when acquiring tokens.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="innerHandler">The inner handler to delegate to.</param>
    /// <param name="credential">
    /// Optional token credential to use. Defaults to <see cref="DefaultAzureCredential"/>.
    /// </param>
    public DefaultAzureCredentialAuthHandler(
        string[] scopes,
        ILogger logger,
        HttpMessageHandler innerHandler,
        TokenCredential? credential = null)
        : base(innerHandler)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credential = credential ?? new DefaultAzureCredential();
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokenRequestContext = new TokenRequestContext(_scopes);
            var accessToken = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
            ProxyLogger.DefaultAzureCredentialTokenAcquired(_logger, _scopes[0]);
        }
        catch (Exception ex)
        {
            ProxyLogger.DefaultAzureCredentialTokenFailed(_logger, ex);
            throw;
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
