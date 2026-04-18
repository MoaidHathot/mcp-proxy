using McpProxy.Sdk.Logging;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpProxy.Sdk.Proxy;

/// <summary>
/// Tracks resource subscriptions and maps them to backend servers.
/// </summary>
public sealed class ResourceSubscriptionManager
{
    private readonly ILogger<ResourceSubscriptionManager> _logger;
    private readonly McpClientManager _clientManager;
    private readonly ConcurrentDictionary<string, string> _subscriptionServerMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceSubscriptionManager"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="clientManager">The client manager.</param>
    public ResourceSubscriptionManager(
        ILogger<ResourceSubscriptionManager> logger,
        McpClientManager clientManager)
    {
        _logger = logger;
        _clientManager = clientManager;
    }

    /// <summary>
    /// Subscribes to a resource on the appropriate backend server.
    /// </summary>
    /// <param name="uri">The resource URI to subscribe to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if subscription was successful, false if resource was not found.</returns>
    public async Task<bool> SubscribeAsync(string uri, CancellationToken cancellationToken = default)
    {
        ProxyLogger.SubscribingToResource(_logger, uri);

        // Find which server owns this resource
        var serverName = await FindResourceServerAsync(uri, cancellationToken).ConfigureAwait(false);

        if (serverName is null)
        {
            ProxyLogger.ResourceNotFoundForSubscription(_logger, uri);
            return false;
        }

        // Get the client and subscribe
        var clientInfo = _clientManager.GetClient(serverName);
        if (clientInfo is null)
        {
            ProxyLogger.BackendNotFoundForSubscription(_logger, serverName, uri);
            return false;
        }

        try
        {
            await clientInfo.Client.SubscribeToResourceAsync(uri, cancellationToken).ConfigureAwait(false);
            _subscriptionServerMap[uri] = serverName;
            ProxyLogger.SubscribedToResource(_logger, uri, serverName);
            return true;
        }
        catch (Exception ex)
        {
            ProxyLogger.ResourceSubscriptionFailed(_logger, uri, serverName, ex);
            return false;
        }
    }

    /// <summary>
    /// Unsubscribes from a resource on the appropriate backend server.
    /// </summary>
    /// <param name="uri">The resource URI to unsubscribe from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if unsubscription was successful, false if resource was not subscribed.</returns>
    public async Task<bool> UnsubscribeAsync(string uri, CancellationToken cancellationToken = default)
    {
        ProxyLogger.UnsubscribingFromResource(_logger, uri);

        // Check if we have a subscription for this resource
        if (!_subscriptionServerMap.TryRemove(uri, out var serverName))
        {
            // Try to find the server from available backends
            serverName = await FindResourceServerAsync(uri, cancellationToken).ConfigureAwait(false);
            if (serverName is null)
            {
                ProxyLogger.NoSubscriptionToUnsubscribe(_logger, uri);
                return false;
            }
        }

        // Get the client and unsubscribe
        var clientInfo = _clientManager.GetClient(serverName);
        if (clientInfo is null)
        {
            ProxyLogger.BackendNotFoundForUnsubscription(_logger, serverName, uri);
            return false;
        }

        try
        {
            await clientInfo.Client.UnsubscribeFromResourceAsync(uri, cancellationToken).ConfigureAwait(false);
            ProxyLogger.UnsubscribedFromResource(_logger, uri, serverName);
            return true;
        }
        catch (Exception ex)
        {
            ProxyLogger.ResourceUnsubscriptionFailed(_logger, uri, serverName, ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the server name for an active subscription.
    /// </summary>
    /// <param name="uri">The resource URI.</param>
    /// <returns>The server name, or null if not subscribed.</returns>
    public string? GetSubscriptionServer(string uri)
    {
        return _subscriptionServerMap.TryGetValue(uri, out var serverName) ? serverName : null;
    }

    /// <summary>
    /// Gets all active subscriptions.
    /// </summary>
    /// <returns>A dictionary of resource URIs to server names.</returns>
    public IReadOnlyDictionary<string, string> GetActiveSubscriptions()
    {
        return _subscriptionServerMap;
    }

    /// <summary>
    /// Finds which server owns a resource by its URI.
    /// </summary>
    private async Task<string?> FindResourceServerAsync(string uri, CancellationToken cancellationToken)
    {
        foreach (var (serverName, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var resources = await clientInfo.Client.ListResourcesAsync(cancellationToken).ConfigureAwait(false);
                var resource = resources.FirstOrDefault(r => string.Equals(r.Uri, uri, StringComparison.OrdinalIgnoreCase));

                if (resource is not null)
                {
                    return serverName;
                }
            }
            catch (Exception ex)
            {
                ProxyLogger.FindResourceServerSearchFailed(_logger, serverName, ex);
            }
        }

        return null;
    }
}
