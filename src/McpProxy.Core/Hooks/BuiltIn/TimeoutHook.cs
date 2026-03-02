using McpProxy.Abstractions;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Hooks.BuiltIn;

/// <summary>
/// Configuration for the timeout hook.
/// </summary>
public sealed class TimeoutConfiguration
{
    /// <summary>
    /// Gets or sets the default timeout in seconds for tool invocations.
    /// Default is 30 seconds.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets per-tool timeout overrides.
    /// Keys can be exact tool names or wildcard patterns (e.g., "slow_*").
    /// Values are timeout in seconds.
    /// </summary>
    public Dictionary<string, int> PerTool { get; set; } = [];
}

/// <summary>
/// A pre-invoke hook that enforces timeout limits on tool invocations.
/// Creates a linked CancellationTokenSource with a timeout that cancels
/// the operation if it exceeds the configured time limit.
/// </summary>
public sealed class TimeoutHook : IPreInvokeHook
{
    private readonly ILogger _logger;
    private readonly TimeoutConfiguration _config;

    /// <summary>
    /// The key used to store the CancellationTokenSource in the context Items dictionary.
    /// </summary>
    public const string TimeoutCtsKey = "McpProxy.Timeout.Cts";

    /// <summary>
    /// Initializes a new instance of <see cref="TimeoutHook"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The timeout configuration.</param>
    public TimeoutHook(ILogger logger, TimeoutConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <inheritdoc />
    public int Priority => -800; // Execute early, after rate limiting

    /// <inheritdoc />
    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var timeoutSeconds = GetTimeoutForTool(context.ToolName);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Create a linked CancellationTokenSource with timeout
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        linkedCts.CancelAfter(timeout);

        // Store the CTS for potential cleanup and update the context's token
        context.Items[TimeoutCtsKey] = linkedCts;
        context.CancellationToken = linkedCts.Token;

        ProxyLogger.TimeoutConfigured(_logger, context.ToolName, timeoutSeconds);
        return ValueTask.CompletedTask;
    }

    private int GetTimeoutForTool(string toolName)
    {
        // Check for exact match first
        if (_config.PerTool.TryGetValue(toolName, out var exactTimeout))
        {
            return exactTimeout;
        }

        // Check for wildcard patterns
        foreach (var (pattern, timeout) in _config.PerTool)
        {
            if (MatchesWildcard(toolName, pattern))
            {
                return timeout;
            }
        }

        return _config.DefaultTimeoutSeconds;
    }

    private static bool MatchesWildcard(string input, string pattern)
    {
        // Simple wildcard matching: only supports trailing * (prefix matching)
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Also support leading * (suffix matching)
        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        // Exact match (case-insensitive)
        return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
