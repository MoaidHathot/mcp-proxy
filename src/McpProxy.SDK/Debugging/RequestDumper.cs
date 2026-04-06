using System.Text.Json;
using System.Text.Json.Serialization;
using McpProxy.SDK.Configuration;
using McpProxy.SDK.Logging;
using Microsoft.Extensions.Logging;

namespace McpProxy.SDK.Debugging;

/// <summary>
/// Implementation of <see cref="IRequestDumper"/> that dumps request and response payloads.
/// </summary>
public sealed class RequestDumper : IRequestDumper
{
    private readonly ILogger<RequestDumper> _logger;
    private readonly DumpConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="RequestDumper"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="config">The dump configuration.</param>
    public RequestDumper(ILogger<RequestDumper> logger, DumpConfiguration config)
    {
        _logger = logger;
        _config = config;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = config.PrettyPrint,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc/>
    public async ValueTask DumpRequestAsync(string serverName, string toolName, object request, CancellationToken cancellationToken = default)
    {
        if (!ShouldDump(serverName, toolName))
        {
            return;
        }

        var json = SerializeWithLimit(request);
        await WriteOutputAsync("REQUEST", serverName, toolName, json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DumpResponseAsync(string serverName, string toolName, object response, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (!ShouldDump(serverName, toolName))
        {
            return;
        }

        var json = SerializeWithLimit(response);
        var header = $"RESPONSE (duration: {duration.TotalMilliseconds:F2}ms)";
        await WriteOutputAsync(header, serverName, toolName, json, cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldDump(string serverName, string toolName)
    {
        if (!_config.Enabled)
        {
            return false;
        }

        // Check server filter
        if (_config.ServerFilter is { Length: > 0 })
        {
            var matchesServer = false;
            foreach (var filter in _config.ServerFilter)
            {
                if (string.Equals(filter, serverName, StringComparison.OrdinalIgnoreCase))
                {
                    matchesServer = true;
                    break;
                }
            }
            if (!matchesServer)
            {
                return false;
            }
        }

        // Check tool filter
        if (_config.ToolFilter is { Length: > 0 })
        {
            var matchesTool = false;
            foreach (var filter in _config.ToolFilter)
            {
                if (string.Equals(filter, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    matchesTool = true;
                    break;
                }
            }
            if (!matchesTool)
            {
                return false;
            }
        }

        return true;
    }

    private string SerializeWithLimit(object obj)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(obj, _jsonOptions);
        }
        catch (JsonException)
        {
            json = obj.ToString() ?? "null";
        }

        var maxBytes = _config.MaxPayloadSizeKb * 1024;
        if (json.Length > maxBytes)
        {
            return json[..maxBytes] + "\n... [TRUNCATED]";
        }

        return json;
    }

    private async ValueTask WriteOutputAsync(string type, string serverName, string toolName, string content, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");

        if (_config.OutputDirectory is not null)
        {
            var safeType = type.Split(' ')[0].ToLowerInvariant();
            var filename = $"{timestamp}_{serverName}_{toolName}_{safeType}.json";
            var path = Path.Combine(_config.OutputDirectory, filename);

            // Ensure directory exists
            Directory.CreateDirectory(_config.OutputDirectory);

            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
            ProxyLogger.DumpWrittenToFile(_logger, path);
        }
        else
        {
            ProxyLogger.DumpToConsole(_logger, type, serverName, toolName, content);
        }
    }
}

/// <summary>
/// No-op implementation of <see cref="IRequestDumper"/> used when dumping is disabled.
/// </summary>
public sealed class NullRequestDumper : IRequestDumper
{
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static readonly NullRequestDumper Instance = new();

    private NullRequestDumper() { }

    /// <inheritdoc/>
    public ValueTask DumpRequestAsync(string serverName, string toolName, object request, CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc/>
    public ValueTask DumpResponseAsync(string serverName, string toolName, object response, TimeSpan duration, CancellationToken cancellationToken = default)
        => default;
}
