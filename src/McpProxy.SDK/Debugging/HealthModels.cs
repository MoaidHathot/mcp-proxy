using System.Text.Json.Serialization;

namespace McpProxy.SDK.Debugging;

/// <summary>
/// Overall proxy health status for the debug endpoint.
/// </summary>
public sealed class ProxyHealthStatus
{
    /// <summary>
    /// Gets or sets the overall health status.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the proxy uptime in seconds.
    /// </summary>
    [JsonPropertyName("uptimeSeconds")]
    public required double UptimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the status was generated.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the proxy version.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the total number of requests processed.
    /// </summary>
    [JsonPropertyName("totalRequests")]
    public required long TotalRequests { get; set; }

    /// <summary>
    /// Gets or sets the total number of failed requests.
    /// </summary>
    [JsonPropertyName("failedRequests")]
    public required long FailedRequests { get; set; }

    /// <summary>
    /// Gets or sets the number of active connections.
    /// </summary>
    [JsonPropertyName("activeConnections")]
    public required int ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets the health status of each backend server.
    /// </summary>
    [JsonPropertyName("backends")]
    public required Dictionary<string, BackendHealthStatus> Backends { get; set; }
}

/// <summary>
/// Health status for an individual backend MCP server.
/// </summary>
public sealed class BackendHealthStatus
{
    /// <summary>
    /// Gets or sets the backend server name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the health status (healthy, unhealthy, unknown, connecting).
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets whether the backend is currently connected.
    /// </summary>
    [JsonPropertyName("connected")]
    public required bool Connected { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last successful request.
    /// </summary>
    [JsonPropertyName("lastSuccessfulRequest")]
    public DateTimeOffset? LastSuccessfulRequest { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last failed request.
    /// </summary>
    [JsonPropertyName("lastFailedRequest")]
    public DateTimeOffset? LastFailedRequest { get; set; }

    /// <summary>
    /// Gets or sets the total requests sent to this backend.
    /// </summary>
    [JsonPropertyName("totalRequests")]
    public required long TotalRequests { get; set; }

    /// <summary>
    /// Gets or sets the total failed requests for this backend.
    /// </summary>
    [JsonPropertyName("failedRequests")]
    public required long FailedRequests { get; set; }

    /// <summary>
    /// Gets or sets the average response time in milliseconds.
    /// </summary>
    [JsonPropertyName("averageResponseTimeMs")]
    public double? AverageResponseTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the number of tools exposed by this backend.
    /// </summary>
    [JsonPropertyName("toolCount")]
    public int? ToolCount { get; set; }

    /// <summary>
    /// Gets or sets the number of prompts exposed by this backend.
    /// </summary>
    [JsonPropertyName("promptCount")]
    public int? PromptCount { get; set; }

    /// <summary>
    /// Gets or sets the number of resources exposed by this backend.
    /// </summary>
    [JsonPropertyName("resourceCount")]
    public int? ResourceCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message if any.
    /// </summary>
    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the consecutive failure count.
    /// </summary>
    [JsonPropertyName("consecutiveFailures")]
    public required int ConsecutiveFailures { get; set; }
}

/// <summary>
/// Constants for health status values.
/// </summary>
public static class HealthStatus
{
    /// <summary>
    /// The service is healthy and operational.
    /// </summary>
    public const string Healthy = "healthy";

    /// <summary>
    /// The service is unhealthy or experiencing issues.
    /// </summary>
    public const string Unhealthy = "unhealthy";

    /// <summary>
    /// The service health is degraded but still functional.
    /// </summary>
    public const string Degraded = "degraded";

    /// <summary>
    /// The service health status is unknown.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The service is currently connecting.
    /// </summary>
    public const string Connecting = "connecting";
}
