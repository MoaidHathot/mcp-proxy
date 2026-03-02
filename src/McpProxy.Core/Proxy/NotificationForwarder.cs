using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProxy.Core.Proxy;

/// <summary>
/// Forwards MCP notifications from backend servers to connected clients.
/// </summary>
public sealed class NotificationForwarder : INotificationForwarder
{
    private readonly ILogger<NotificationForwarder> _logger;
    private McpServer? _mcpServer;

    /// <summary>
    /// Initializes a new instance of <see cref="NotificationForwarder"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public NotificationForwarder(ILogger<NotificationForwarder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the MCP server instance that will be used to forward notifications to clients.
    /// </summary>
    /// <param name="mcpServer">The MCP server instance.</param>
    public void SetMcpServer(McpServer mcpServer)
    {
        _mcpServer = mcpServer;
    }

    /// <inheritdoc />
    public async Task ForwardNotificationAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken = default)
    {
        if (_mcpServer is null)
        {
            ProxyLogger.NotificationForwardingSkipped(_logger, method, "MCP server not initialized");
            return;
        }

        try
        {
            ProxyLogger.ForwardingNotification(_logger, method);
#pragma warning disable CA2016 // SendNotificationAsync does not accept cancellation token
            await _mcpServer.SendNotificationAsync(method, parameters).ConfigureAwait(false);
#pragma warning restore CA2016
            ProxyLogger.NotificationForwarded(_logger, method);
        }
        catch (Exception ex)
        {
            ProxyLogger.NotificationForwardingFailed(_logger, method, ex);
        }
    }

    /// <inheritdoc />
    public async Task ForwardProgressNotificationAsync(
        string progressToken,
        double progress,
        double? total = null,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        if (_mcpServer is null)
        {
            ProxyLogger.ProgressNotificationSkipped(_logger, progressToken, "MCP server not initialized");
            return;
        }

        try
        {
            ProxyLogger.ForwardingProgressNotification(_logger, progressToken, progress, total);

            var progressValue = new ProgressNotificationValue
            {
                Progress = (float)progress,
                Total = total.HasValue ? (float)total.Value : null,
                Message = message
            };

            var progressParams = new ProgressNotificationParams
            {
                ProgressToken = new ProgressToken(progressToken),
                Progress = progressValue
            };

#pragma warning disable CA2016 // SendNotificationAsync does not accept cancellation token
            await _mcpServer.SendNotificationAsync(
                NotificationMethods.ProgressNotification,
                progressParams).ConfigureAwait(false);
#pragma warning restore CA2016

            ProxyLogger.ProgressNotificationForwarded(_logger, progressToken);
        }
        catch (Exception ex)
        {
            ProxyLogger.ProgressNotificationFailed(_logger, progressToken, ex);
        }
    }

    /// <summary>
    /// Creates a notification handler that forwards progress notifications from a backend server.
    /// </summary>
    /// <param name="serverName">The name of the backend server.</param>
    /// <returns>A notification handler delegate compatible with McpClient.RegisterNotificationHandler.</returns>
    public Func<JsonRpcNotification, CancellationToken, ValueTask> CreateProgressNotificationHandler(string serverName)
    {
        return async (notification, cancellationToken) =>
        {
            if (notification.Params is null)
            {
                return;
            }

            ProxyLogger.ReceivedProgressNotification(_logger, serverName);

            // Parse the progress notification parameters
            try
            {
                var progressParams = JsonSerializer.Deserialize<ProgressNotificationParams>(notification.Params);

                if (progressParams is not null)
                {
                    var tokenString = progressParams.ProgressToken.ToString() ?? string.Empty;
                    await ForwardProgressNotificationAsync(
                        tokenString,
                        progressParams.Progress.Progress,
                        progressParams.Progress.Total,
                        progressParams.Progress.Message,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (JsonException ex)
            {
                ProxyLogger.ProgressNotificationParsingFailed(_logger, serverName, ex);
            }
        };
    }

    /// <summary>
    /// Creates a generic notification handler that forwards notifications from a backend server.
    /// </summary>
    /// <param name="serverName">The name of the backend server.</param>
    /// <param name="method">The notification method to forward.</param>
    /// <returns>A notification handler delegate compatible with McpClient.RegisterNotificationHandler.</returns>
    public Func<JsonRpcNotification, CancellationToken, ValueTask> CreateNotificationHandler(string serverName, string method)
    {
        return async (notification, cancellationToken) =>
        {
            ProxyLogger.ReceivedNotification(_logger, method, serverName);

            // Convert JsonNode to JsonElement for forwarding
            JsonElement? paramsElement = null;
            if (notification.Params is not null)
            {
                var jsonString = notification.Params.ToJsonString();
                paramsElement = JsonSerializer.Deserialize<JsonElement>(jsonString);
            }

            await ForwardNotificationAsync(method, paramsElement, cancellationToken).ConfigureAwait(false);
        };
    }
}
