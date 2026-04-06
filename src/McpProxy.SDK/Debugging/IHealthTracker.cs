namespace McpProxy.SDK.Debugging;

/// <summary>
/// Interface for tracking proxy and backend health status.
/// </summary>
public interface IHealthTracker
{
    /// <summary>
    /// Gets the current health status of the proxy and all backends.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current proxy health status.</returns>
    Task<ProxyHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful request to a backend.
    /// </summary>
    /// <param name="backendName">The backend server name.</param>
    /// <param name="responseTimeMs">The response time in milliseconds.</param>
    void RecordSuccess(string backendName, double responseTimeMs);

    /// <summary>
    /// Records a failed request to a backend.
    /// </summary>
    /// <param name="backendName">The backend server name.</param>
    /// <param name="errorMessage">The error message.</param>
    void RecordFailure(string backendName, string? errorMessage);

    /// <summary>
    /// Records a backend connection state change.
    /// </summary>
    /// <param name="backendName">The backend server name.</param>
    /// <param name="connected">Whether the backend is now connected.</param>
    void RecordConnectionState(string backendName, bool connected);

    /// <summary>
    /// Records the capabilities of a backend (tools, prompts, resources counts).
    /// </summary>
    /// <param name="backendName">The backend server name.</param>
    /// <param name="toolCount">The number of tools.</param>
    /// <param name="promptCount">The number of prompts.</param>
    /// <param name="resourceCount">The number of resources.</param>
    void RecordCapabilities(string backendName, int toolCount, int promptCount, int resourceCount);

    /// <summary>
    /// Increments the active connection count.
    /// </summary>
    void IncrementActiveConnections();

    /// <summary>
    /// Decrements the active connection count.
    /// </summary>
    void DecrementActiveConnections();
}
