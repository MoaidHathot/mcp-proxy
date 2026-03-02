using System.Text.Json;

namespace McpProxy.Abstractions;

/// <summary>
/// Interface for forwarding MCP notifications from backend servers to connected clients.
/// </summary>
public interface INotificationForwarder
{
    /// <summary>
    /// Forwards a notification to connected clients.
    /// </summary>
    /// <param name="method">The notification method name.</param>
    /// <param name="parameters">The notification parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ForwardNotificationAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards a progress notification to connected clients.
    /// </summary>
    /// <param name="progressToken">The progress token identifying the operation.</param>
    /// <param name="progress">The progress value (0-100 or custom).</param>
    /// <param name="total">The total value for percentage calculation.</param>
    /// <param name="message">Optional progress message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ForwardProgressNotificationAsync(
        string progressToken,
        double progress,
        double? total = null,
        string? message = null,
        CancellationToken cancellationToken = default);
}
