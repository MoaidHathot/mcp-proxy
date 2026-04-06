using System.Collections.Concurrent;
using System.Reflection;
using McpProxy.Sdk.Logging;
using Microsoft.Extensions.Logging;

namespace McpProxy.Sdk.Debugging;

/// <summary>
/// Implementation of <see cref="IHealthTracker"/> that tracks proxy and backend health.
/// </summary>
public sealed class HealthTracker : IHealthTracker
{
    private readonly ILogger _logger;
    private readonly DateTimeOffset _startTime;
    private readonly ConcurrentDictionary<string, BackendStats> _backendStats;
    private readonly string? _version;
    private long _totalRequests;
    private long _failedRequests;
    private int _activeConnections;

    /// <summary>
    /// Initializes a new instance of <see cref="HealthTracker"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public HealthTracker(ILogger<HealthTracker> logger)
    {
        _logger = logger;
        _startTime = DateTimeOffset.UtcNow;
        _backendStats = new ConcurrentDictionary<string, BackendStats>(StringComparer.OrdinalIgnoreCase);
        _version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
    }

    /// <inheritdoc/>
    public Task<ProxyHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        var backends = new Dictionary<string, BackendHealthStatus>();

        foreach (var kvp in _backendStats)
        {
            var stats = kvp.Value;
            backends[kvp.Key] = new BackendHealthStatus
            {
                Name = kvp.Key,
                Status = DetermineBackendStatus(stats),
                Connected = stats.IsConnected,
                LastSuccessfulRequest = stats.LastSuccessfulRequest,
                LastFailedRequest = stats.LastFailedRequest,
                TotalRequests = Interlocked.Read(ref stats.TotalRequests),
                FailedRequests = Interlocked.Read(ref stats.FailedRequests),
                AverageResponseTimeMs = stats.GetAverageResponseTime(),
                ToolCount = stats.ToolCount,
                PromptCount = stats.PromptCount,
                ResourceCount = stats.ResourceCount,
                LastError = stats.LastError,
                ConsecutiveFailures = stats.ConsecutiveFailures
            };
        }

        var overallStatus = DetermineOverallStatus(backends.Values);
        var uptime = DateTimeOffset.UtcNow - _startTime;

        var status = new ProxyHealthStatus
        {
            Status = overallStatus,
            UptimeSeconds = uptime.TotalSeconds,
            Timestamp = DateTimeOffset.UtcNow,
            Version = _version,
            TotalRequests = Interlocked.Read(ref _totalRequests),
            FailedRequests = Interlocked.Read(ref _failedRequests),
            ActiveConnections = Volatile.Read(ref _activeConnections),
            Backends = backends
        };

        return Task.FromResult(status);
    }

    /// <inheritdoc/>
    public void RecordSuccess(string backendName, double responseTimeMs)
    {
        var stats = GetOrCreateStats(backendName);
        Interlocked.Increment(ref stats.TotalRequests);
        Interlocked.Increment(ref _totalRequests);
        stats.LastSuccessfulRequest = DateTimeOffset.UtcNow;
        stats.ConsecutiveFailures = 0;
        stats.RecordResponseTime(responseTimeMs);

        ProxyLogger.HealthRecordedSuccess(_logger, backendName, responseTimeMs);
    }

    /// <inheritdoc/>
    public void RecordFailure(string backendName, string? errorMessage)
    {
        var stats = GetOrCreateStats(backendName);
        Interlocked.Increment(ref stats.TotalRequests);
        Interlocked.Increment(ref stats.FailedRequests);
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);
        stats.LastFailedRequest = DateTimeOffset.UtcNow;
        stats.LastError = errorMessage;
        Interlocked.Increment(ref stats.ConsecutiveFailures);

        ProxyLogger.HealthRecordedFailure(_logger, backendName, errorMessage ?? "Unknown error");
    }

    /// <inheritdoc/>
    public void RecordConnectionState(string backendName, bool connected)
    {
        var stats = GetOrCreateStats(backendName);
        stats.IsConnected = connected;

        if (connected)
        {
            stats.ConsecutiveFailures = 0;
            ProxyLogger.HealthBackendConnected(_logger, backendName);
        }
        else
        {
            ProxyLogger.HealthBackendDisconnected(_logger, backendName);
        }
    }

    /// <inheritdoc/>
    public void RecordCapabilities(string backendName, int toolCount, int promptCount, int resourceCount)
    {
        var stats = GetOrCreateStats(backendName);
        stats.ToolCount = toolCount;
        stats.PromptCount = promptCount;
        stats.ResourceCount = resourceCount;

        ProxyLogger.HealthRecordedCapabilities(_logger, backendName, toolCount, promptCount, resourceCount);
    }

    /// <inheritdoc/>
    public void IncrementActiveConnections()
    {
        Interlocked.Increment(ref _activeConnections);
    }

    /// <inheritdoc/>
    public void DecrementActiveConnections()
    {
        Interlocked.Decrement(ref _activeConnections);
    }

    private BackendStats GetOrCreateStats(string backendName)
    {
        return _backendStats.GetOrAdd(backendName, _ => new BackendStats());
    }

    private static string DetermineBackendStatus(BackendStats stats)
    {
        if (!stats.IsConnected)
        {
            return HealthStatus.Unknown;
        }

        if (stats.ConsecutiveFailures >= 5)
        {
            return HealthStatus.Unhealthy;
        }

        if (stats.ConsecutiveFailures >= 2)
        {
            return HealthStatus.Degraded;
        }

        return HealthStatus.Healthy;
    }

    private static string DetermineOverallStatus(IEnumerable<BackendHealthStatus> backends)
    {
        var backendList = backends.ToList();

        if (backendList.Count == 0)
        {
            return HealthStatus.Unknown;
        }

        var unhealthyCount = backendList.Count(b => b.Status == HealthStatus.Unhealthy);
        var degradedCount = backendList.Count(b => b.Status == HealthStatus.Degraded);
        var unknownCount = backendList.Count(b => b.Status == HealthStatus.Unknown);

        // If all backends are unhealthy, proxy is unhealthy
        if (unhealthyCount == backendList.Count)
        {
            return HealthStatus.Unhealthy;
        }

        // If any backend is unhealthy or degraded, proxy is degraded
        if (unhealthyCount > 0 || degradedCount > 0)
        {
            return HealthStatus.Degraded;
        }

        // If all backends are unknown, proxy is unknown
        if (unknownCount == backendList.Count)
        {
            return HealthStatus.Unknown;
        }

        return HealthStatus.Healthy;
    }

    private sealed class BackendStats
    {
        public long TotalRequests;
        public long FailedRequests;
        public volatile bool IsConnected;
        public DateTimeOffset? LastSuccessfulRequest;
        public DateTimeOffset? LastFailedRequest;
        public string? LastError;
        public int ConsecutiveFailures;
        public int? ToolCount;
        public int? PromptCount;
        public int? ResourceCount;

        private readonly object _responseLock = new();
        private double _totalResponseTime;
        private long _responseTimeCount;

        public void RecordResponseTime(double responseTimeMs)
        {
            lock (_responseLock)
            {
                _totalResponseTime += responseTimeMs;
                _responseTimeCount++;
            }
        }

        public double? GetAverageResponseTime()
        {
            lock (_responseLock)
            {
                if (_responseTimeCount == 0)
                {
                    return null;
                }

                return _totalResponseTime / _responseTimeCount;
            }
        }
    }
}

/// <summary>
/// A null implementation of <see cref="IHealthTracker"/> that does nothing.
/// Used when health tracking is disabled.
/// </summary>
public sealed class NullHealthTracker : IHealthTracker
{
    /// <summary>
    /// Singleton instance of <see cref="NullHealthTracker"/>.
    /// </summary>
    public static readonly NullHealthTracker Instance = new();

    private NullHealthTracker() { }

    /// <inheritdoc/>
    public Task<ProxyHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProxyHealthStatus
        {
            Status = HealthStatus.Unknown,
            UptimeSeconds = 0,
            Timestamp = DateTimeOffset.UtcNow,
            TotalRequests = 0,
            FailedRequests = 0,
            ActiveConnections = 0,
            Backends = new Dictionary<string, BackendHealthStatus>()
        });
    }

    /// <inheritdoc/>
    public void RecordSuccess(string backendName, double responseTimeMs) { }

    /// <inheritdoc/>
    public void RecordFailure(string backendName, string? errorMessage) { }

    /// <inheritdoc/>
    public void RecordConnectionState(string backendName, bool connected) { }

    /// <inheritdoc/>
    public void RecordCapabilities(string backendName, int toolCount, int promptCount, int resourceCount) { }

    /// <inheritdoc/>
    public void IncrementActiveConnections() { }

    /// <inheritdoc/>
    public void DecrementActiveConnections() { }
}
