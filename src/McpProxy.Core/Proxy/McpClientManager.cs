using McpProxy.Core.Configuration;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Proxy;

/// <summary>
/// Manages connections to backend MCP servers.
/// </summary>
public sealed class McpClientManager : IAsyncDisposable
{
    private readonly ILogger<McpClientManager> _logger;
    private readonly ProxyClientHandlers? _proxyClientHandlers;
    private readonly Dictionary<string, McpClientInfo> _clients = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="McpClientManager"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="proxyClientHandlers">Optional handlers for forwarding sampling/elicitation/roots requests.</param>
    public McpClientManager(ILogger<McpClientManager> logger, ProxyClientHandlers? proxyClientHandlers = null)
    {
        _logger = logger;
        _proxyClientHandlers = proxyClientHandlers;
    }

    /// <summary>
    /// Gets all connected clients.
    /// </summary>
    public IReadOnlyDictionary<string, McpClientInfo> Clients => _clients;

    /// <summary>
    /// Initializes connections to all configured backend servers.
    /// </summary>
    /// <param name="configuration">The proxy configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(ProxyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (name, serverConfig) in configuration.Mcp)
            {
                if (!serverConfig.Enabled)
                {
                    continue;
                }

                try
                {
                    var client = await CreateClientAsync(name, serverConfig, cancellationToken).ConfigureAwait(false);
                    _clients[name] = client;
                }
                catch (Exception ex)
                {
                    ProxyLogger.BackendConnectionFailed(_logger, name, ex);
                    throw;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets a client by server name.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>The client info, or null if not found.</returns>
    public McpClientInfo? GetClient(string serverName)
    {
        return _clients.TryGetValue(serverName, out var client) ? client : null;
    }

    /// <summary>
    /// Creates a client connection to a backend server.
    /// </summary>
    private async Task<McpClientInfo> CreateClientAsync(
        string name,
        ServerConfiguration config,
        CancellationToken cancellationToken)
    {
        var transportType = config.Type.ToString();
        ProxyLogger.ConnectingToBackend(_logger, name, transportType);

        IClientTransport transport = config.Type switch
        {
            ServerTransportType.Stdio => CreateStdioTransport(name, config),
            ServerTransportType.Http or ServerTransportType.Sse => CreateHttpTransport(name, config),
            _ => throw new NotSupportedException($"Unsupported transport type: {config.Type}")
        };

        var clientOptions = CreateClientOptions();
        var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

        ProxyLogger.ConnectedToBackend(_logger, name);

        return new McpClientInfo
        {
            Name = name,
            Configuration = config,
            Client = new McpClientWrapper(client)
        };
    }

    /// <summary>
    /// Registers a client directly with the manager. Used for testing.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="client">The client wrapper.</param>
    /// <param name="config">The server configuration.</param>
    public void RegisterClient(string serverName, IMcpClientWrapper client, ServerConfiguration config)
    {
        _clients[serverName] = new McpClientInfo
        {
            Name = serverName,
            Configuration = config,
            Client = client
        };
    }

    /// <summary>
    /// Creates client options with handlers for sampling, elicitation, and roots.
    /// </summary>
    private McpClientOptions CreateClientOptions()
    {
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "McpProxy",
                Version = "1.0.0"
            }
        };

        // Only configure capabilities and handlers if we have proxy client handlers
        if (_proxyClientHandlers is not null)
        {
            options.Capabilities = new ClientCapabilities
            {
                // Enable sampling capability - allows backend servers to request LLM completions
                Sampling = new SamplingCapability(),
                // Enable elicitation capability - allows backend servers to request user input
                Elicitation = new ElicitationCapability(),
                // Enable roots capability - allows backend servers to request file system roots
                Roots = new RootsCapability()
            };

            options.Handlers = new McpClientHandlers
            {
                SamplingHandler = _proxyClientHandlers.HandleSamplingAsync,
                ElicitationHandler = _proxyClientHandlers.HandleElicitationAsync,
                RootsHandler = _proxyClientHandlers.HandleRootsAsync
            };

            ProxyLogger.ClientHandlersConfigured(_logger);
        }

        return options;
    }

    /// <summary>
    /// Creates a STDIO transport for a backend server.
    /// </summary>
    private static StdioClientTransport CreateStdioTransport(string name, ServerConfiguration config)
    {
        var options = new StdioClientTransportOptions
        {
            Name = name,
            Command = config.Command!,
            Arguments = config.Arguments
        };

        if (config.Environment is { Count: > 0 })
        {
            foreach (var (key, value) in config.Environment)
            {
                options.EnvironmentVariables ??= new Dictionary<string, string?>();
                options.EnvironmentVariables[key] = value;
            }
        }

        return new StdioClientTransport(options);
    }

    /// <summary>
    /// Creates an HTTP/SSE transport for a backend server.
    /// </summary>
    private static HttpClientTransport CreateHttpTransport(string name, ServerConfiguration config)
    {
        var transportMode = config.Type == ServerTransportType.Sse
            ? HttpTransportMode.Sse
            : HttpTransportMode.AutoDetect;

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(config.Url!),
            Name = name,
            TransportMode = transportMode
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var (name, clientInfo) in _clients)
        {
            try
            {
                await clientInfo.Client.DisposeAsync().ConfigureAwait(false);
                ProxyLogger.BackendDisconnected(_logger, name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing client {ServerName}", name);
            }
        }

        _clients.Clear();
        _lock.Dispose();
    }
}
