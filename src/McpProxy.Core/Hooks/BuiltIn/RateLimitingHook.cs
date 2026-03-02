using McpProxy.Abstractions;
using McpProxy.Core.Exceptions;
using McpProxy.Core.Logging;
using McpProxy.Core.Telemetry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Hooks.BuiltIn;

/// <summary>
/// Specifies how the rate limit key is generated.
/// </summary>
public enum RateLimitKeyType
{
    /// <summary>
    /// Rate limit by client/principal ID.
    /// </summary>
    Client,

    /// <summary>
    /// Rate limit by tool name.
    /// </summary>
    Tool,

    /// <summary>
    /// Rate limit by server name.
    /// </summary>
    Server,

    /// <summary>
    /// Rate limit by combination of client, server, and tool.
    /// </summary>
    Combined
}

/// <summary>
/// Configuration for the rate limiting hook.
/// </summary>
public sealed class RateLimitingConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of requests allowed in the window.
    /// Default is 100.
    /// </summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>
    /// Gets or sets the window duration in seconds.
    /// Default is 60 seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets how the rate limit key is generated.
    /// Default is Client.
    /// </summary>
    public RateLimitKeyType KeyType { get; set; } = RateLimitKeyType.Client;

    /// <summary>
    /// Gets or sets the error message when rate limit is exceeded.
    /// </summary>
    public string ErrorMessage { get; set; } = "Rate limit exceeded. Please try again later.";
}

/// <summary>
/// A pre-invoke hook that enforces rate limiting on tool invocations.
/// Uses a sliding window algorithm with in-memory caching.
/// </summary>
public sealed class RateLimitingHook : IPreInvokeHook
{
    private readonly IMemoryCache _cache;
    private readonly ILogger _logger;
    private readonly ProxyMetrics? _metrics;
    private readonly RateLimitingConfiguration _config;

    /// <summary>
    /// Initializes a new instance of <see cref="RateLimitingHook"/>.
    /// </summary>
    /// <param name="cache">The memory cache for tracking request counts.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The rate limiting configuration.</param>
    /// <param name="metrics">Optional metrics instance for recording rate limit events.</param>
    public RateLimitingHook(
        IMemoryCache cache,
        ILogger logger,
        RateLimitingConfiguration config,
        ProxyMetrics? metrics = null)
    {
        _cache = cache;
        _logger = logger;
        _config = config;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public int Priority => -900; // Execute early, after logging

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var key = GenerateKey(context);
        var cacheKey = $"ratelimit:{key}";

        // Get or create the counter
        var counter = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_config.WindowSeconds);
            return new RateLimitCounter();
        })!;

        // Increment and check
        var currentCount = counter.Increment();

        if (currentCount > _config.MaxRequests)
        {
            ProxyLogger.RateLimitExceeded(_logger, key, _config.MaxRequests, _config.WindowSeconds);
            _metrics?.RecordRateLimitExceeded(context.ServerName, context.ToolName, _config.KeyType.ToString());

            throw new RateLimitExceededException(key, _config.MaxRequests, _config.WindowSeconds);
        }

        ProxyLogger.RateLimitChecked(_logger, key, currentCount, _config.MaxRequests);
        return ValueTask.CompletedTask;
    }

    private string GenerateKey(HookContext<CallToolRequestParams> context)
    {
        var principalId = context.AuthenticationResult?.PrincipalId ?? "anonymous";

        return _config.KeyType switch
        {
            RateLimitKeyType.Client => principalId,
            RateLimitKeyType.Tool => context.ToolName,
            RateLimitKeyType.Server => context.ServerName,
            RateLimitKeyType.Combined => $"{principalId}:{context.ServerName}:{context.ToolName}",
            _ => principalId
        };
    }

    /// <summary>
    /// Thread-safe counter for rate limiting.
    /// </summary>
    private sealed class RateLimitCounter
    {
        private int _count;

        public int Increment() => Interlocked.Increment(ref _count);
    }
}
