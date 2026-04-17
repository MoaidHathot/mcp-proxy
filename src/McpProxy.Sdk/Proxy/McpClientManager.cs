using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Debugging;
using McpProxy.Sdk.Logging;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProxy.Sdk.Proxy;

/// <summary>
/// Manages connections to backend MCP servers.
/// </summary>
public sealed class McpClientManager : IAsyncDisposable
{
    private readonly ILogger<McpClientManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ProxyClientHandlers? _proxyClientHandlers;
    private readonly NotificationForwarder? _notificationForwarder;
    private readonly IHealthTracker _healthTracker;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly Dictionary<string, McpClientInfo> _clients = [];
    private readonly Dictionary<string, ServerConfiguration> _deferredClients = [];
    private readonly Dictionary<string, InteractiveBrowserCredential> _sharedCredentials = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _disposables = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ClientCapabilitySettings? _capabilitySettings;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="McpClientManager"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="loggerFactory">The logger factory for creating typed loggers.</param>
    /// <param name="proxyClientHandlers">Optional handlers for forwarding sampling/elicitation/roots requests.</param>
    /// <param name="notificationForwarder">Optional notification forwarder for forwarding notifications to clients.</param>
    /// <param name="healthTracker">Optional health tracker for monitoring backend health.</param>
    /// <param name="httpContextAccessor">Optional HTTP context accessor for forwarding authorization headers.</param>
    public McpClientManager(
        ILogger<McpClientManager> logger,
        ILoggerFactory loggerFactory,
        ProxyClientHandlers? proxyClientHandlers = null,
        NotificationForwarder? notificationForwarder = null,
        IHealthTracker? healthTracker = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _proxyClientHandlers = proxyClientHandlers;
        _notificationForwarder = notificationForwarder;
        _healthTracker = healthTracker ?? NullHealthTracker.Instance;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets all connected clients.
    /// </summary>
    public IReadOnlyDictionary<string, McpClientInfo> Clients => _clients;

    /// <summary>
    /// Gets the health tracker.
    /// </summary>
    public IHealthTracker HealthTracker => _healthTracker;

    /// <summary>
    /// Initializes connections to all configured backend servers.
    /// Backends using <see cref="BackendAuthType.ForwardAuthorization"/> are deferred
    /// because no user token is available at startup; they connect lazily on first request.
    /// </summary>
    /// <param name="configuration">The proxy configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(ProxyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _capabilitySettings = configuration.Proxy.Capabilities.Client;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (name, serverConfig) in configuration.Mcp)
            {
                if (!serverConfig.Enabled)
                {
                    continue;
                }

                // ForwardAuthorization backends cannot connect at startup because there is
                // no user token available. Store them for lazy connection on first request.
                if (serverConfig.Auth?.Type == BackendAuthType.ForwardAuthorization)
                {
                    _deferredClients[name] = serverConfig;
                    ProxyLogger.BackendConnectionDeferred(_logger, name);
                    continue;
                }

                try
                {
                    var client = await CreateClientAsync(name, serverConfig, cancellationToken).ConfigureAwait(false);
                    _clients[name] = client;
                }
                catch (Exception ex)
                {
                    // Log and defer — the backend will be retried on first client request.
                    // This prevents startup crashes when a backend is temporarily unreachable
                    // or when credentials need interactive consent.
                    ProxyLogger.BackendConnectionFailed(_logger, name, ex);
                    _deferredClients[name] = serverConfig;
                    ProxyLogger.BackendConnectionDeferred(_logger, name);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Attempts to connect deferred clients. Should be called within request context
    /// where an HTTP context (and Authorization header) may be available.
    /// Connection failures are logged but not thrown — deferred clients remain in the
    /// deferred list for future attempts when authentication becomes available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnsureDeferredClientsConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_deferredClients.Count == 0)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Copy keys to avoid modifying collection during iteration
            var deferredNames = _deferredClients.Keys.ToList();
            foreach (var name in deferredNames)
            {
                if (_clients.ContainsKey(name))
                {
                    _deferredClients.Remove(name);
                    continue;
                }

                var serverConfig = _deferredClients[name];
                try
                {
                    var client = await CreateClientAsync(name, serverConfig, cancellationToken).ConfigureAwait(false);
                    _clients[name] = client;
                    _deferredClients.Remove(name);
                }
                catch (Exception ex)
                {
                    // Log but do not throw — the backend likely requires authentication
                    // that isn't available yet. The client stays deferred and will be
                    // retried on subsequent requests once the caller authenticates.
                    ProxyLogger.BackendConnectionFailed(_logger, name, ex);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets whether there are deferred clients that have not yet connected.
    /// </summary>
    public bool HasDeferredClients => _deferredClients.Count > 0;

    /// <summary>
    /// Gets the names of backends that are still deferred (not yet connected).
    /// </summary>
    public IReadOnlyCollection<string> DeferredClientNames => _deferredClients.Keys;

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

        // Register notification handlers for forwarding
        RegisterNotificationHandlers(client, name);

        ProxyLogger.ConnectedToBackend(_logger, name);

        // Record connection state in health tracker
        _healthTracker.RecordConnectionState(name, connected: true);

        return new McpClientInfo
        {
            Name = name,
            Configuration = config,
            Client = new McpClientWrapper(client)
        };
    }

    /// <summary>
    /// MCP notification method constants.
    /// </summary>
    private static class McpNotificationMethods
    {
        public const string ToolsListChanged = "notifications/tools/list_changed";
        public const string ResourcesListChanged = "notifications/resources/list_changed";
        public const string PromptsListChanged = "notifications/prompts/list_changed";
        public const string ResourceUpdated = "notifications/resources/updated";
    }

    /// <summary>
    /// Registers notification handlers on a client for forwarding to the proxy's connected clients.
    /// </summary>
    private void RegisterNotificationHandlers(McpClient client, string serverName)
    {
        if (_notificationForwarder is null)
        {
            return;
        }

        // Register progress notification handler
        client.RegisterNotificationHandler(
            NotificationMethods.ProgressNotification,
            _notificationForwarder.CreateProgressNotificationHandler(serverName));

        // Register list changed notification handlers
        client.RegisterNotificationHandler(
            McpNotificationMethods.ToolsListChanged,
            _notificationForwarder.CreateNotificationHandler(serverName, McpNotificationMethods.ToolsListChanged));

        client.RegisterNotificationHandler(
            McpNotificationMethods.ResourcesListChanged,
            _notificationForwarder.CreateNotificationHandler(serverName, McpNotificationMethods.ResourcesListChanged));

        client.RegisterNotificationHandler(
            McpNotificationMethods.PromptsListChanged,
            _notificationForwarder.CreateNotificationHandler(serverName, McpNotificationMethods.PromptsListChanged));

        // Register resource updated notification handler
        client.RegisterNotificationHandler(
            McpNotificationMethods.ResourceUpdated,
            _notificationForwarder.CreateNotificationHandler(serverName, McpNotificationMethods.ResourceUpdated));

        ProxyLogger.NotificationHandlersRegistered(_logger, serverName);
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
            var capabilities = new ClientCapabilities();
            var handlers = new McpClientHandlers();

            // Configure sampling capability
            if (_capabilitySettings?.Sampling != false)
            {
                capabilities.Sampling = new SamplingCapability();
                handlers.SamplingHandler = _proxyClientHandlers.HandleSamplingAsync;
            }

            // Configure elicitation capability
            if (_capabilitySettings?.Elicitation != false)
            {
                capabilities.Elicitation = new ElicitationCapability();
                handlers.ElicitationHandler = _proxyClientHandlers.HandleElicitationAsync;
            }

            // Configure roots capability
            if (_capabilitySettings?.Roots != false)
            {
                capabilities.Roots = new RootsCapability();
                handlers.RootsHandler = _proxyClientHandlers.HandleRootsAsync;
            }

            // Configure experimental capabilities
            if (_capabilitySettings?.Experimental is { Count: > 0 })
            {
                capabilities.Experimental = _capabilitySettings.Experimental;
                ProxyLogger.ExperimentalCapabilitiesConfigured(_logger, _capabilitySettings.Experimental.Count);
            }

            options.Capabilities = capabilities;
            options.Handlers = handlers;

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
    private HttpClientTransport CreateHttpTransport(string name, ServerConfiguration config)
    {
        var transportMode = config.Type == ServerTransportType.Sse
            ? HttpTransportMode.Sse
            : HttpTransportMode.AutoDetect;

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(config.Url!),
            Name = name,
            TransportMode = transportMode
        };

        // Check if backend authentication is configured
        if (config.Auth is { Type: not BackendAuthType.None })
        {
            var httpClient = CreateAuthenticatedHttpClient(config.Auth);
            return new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: true);
        }

        return new HttpClientTransport(transportOptions);
    }

    /// <summary>
    /// Creates an HTTP client with authentication configured based on the auth type.
    /// </summary>
    private HttpClient CreateAuthenticatedHttpClient(BackendAuthConfiguration authConfig)
    {
        // Handle ForwardAuthorization separately - it doesn't need Azure AD configuration
        if (authConfig.Type == BackendAuthType.ForwardAuthorization)
        {
            return CreateForwardAuthorizationHttpClient();
        }

        // Handle DefaultAzureCredential separately - uses Azure.Identity instead of MSAL
        if (authConfig.Type == BackendAuthType.AzureDefaultCredential)
        {
            return CreateDefaultAzureCredentialHttpClient(authConfig);
        }

        // Handle InteractiveBrowser - uses InteractiveBrowserCredential from Azure.Identity
        if (authConfig.Type == BackendAuthType.InteractiveBrowser)
        {
            return CreateInteractiveBrowserHttpClient(authConfig);
        }

        var credentialProvider = new AzureAdCredentialProvider(
            authConfig.AzureAd,
            authConfig.Type,
            _loggerFactory.CreateLogger<AzureAdCredentialProvider>());

        // Track for disposal
        _disposables.Add(credentialProvider);

        // TODO: For on-behalf-of flow, we need to pass a user token accessor
        // This requires integration with the request context
        Func<string?>? userTokenAccessor = null;

        var handler = new AzureAdAuthorizationHandler(
            credentialProvider,
            _loggerFactory.CreateLogger<AzureAdAuthorizationHandler>(),
            userTokenAccessor);

        return new HttpClient(handler);
    }

    /// <summary>
    /// Creates an HTTP client that forwards the incoming Authorization header.
    /// </summary>
    private HttpClient CreateForwardAuthorizationHttpClient()
    {
        if (_httpContextAccessor is null)
        {
            throw new InvalidOperationException(
                "ForwardAuthorization requires IHttpContextAccessor. " +
                "This typically means the proxy is running in stdio mode, which does not support " +
                "HTTP header forwarding. Run the proxy in HTTP/SSE mode instead.");
        }

        var handler = new ForwardAuthorizationHandler(
            _httpContextAccessor,
            _loggerFactory.CreateLogger<ForwardAuthorizationHandler>());

        return new HttpClient(handler);
    }

    /// <summary>
    /// Creates an HTTP client that acquires tokens via <see cref="DefaultAzureCredentialAuthHandler"/>.
    /// If a pre-configured <see cref="Azure.Core.TokenCredential"/> is provided in the configuration,
    /// it is used instead of creating a new <c>DefaultAzureCredential</c>.
    /// </summary>
    private HttpClient CreateDefaultAzureCredentialHttpClient(BackendAuthConfiguration authConfig)
    {
        var scopes = authConfig.AzureAd.Scopes ?? [".default"];
        var credential = authConfig.AzureAd.TokenCredential;
        var handler = new DefaultAzureCredentialAuthHandler(
            scopes,
            _loggerFactory.CreateLogger<DefaultAzureCredentialAuthHandler>(),
            credential);

        return new HttpClient(handler);
    }

    /// <summary>
    /// Creates an HTTP client that acquires user-delegated tokens via <see cref="InteractiveBrowserCredential"/>.
    /// Opens a browser for sign-in on first use; subsequent requests use cached refresh tokens persisted
    /// to the OS credential store. This enables non-OAuth MCP clients (stdio) to access backends that
    /// require a pre-authorized public client ID (e.g., Microsoft 365 MCP servers).
    /// </summary>
    private HttpClient CreateInteractiveBrowserHttpClient(BackendAuthConfiguration authConfig)
    {
        var config = authConfig.AzureAd;
        var scopes = config.Scopes ?? [".default"];

        if (string.IsNullOrEmpty(config.ClientId))
        {
            throw new InvalidOperationException(
                "InteractiveBrowser authentication requires a ClientId (the public client app ID). " +
                "Set 'auth.azureAd.clientId' in the server configuration.");
        }

        // Check if a shared credential already exists for this credential group
        var groupName = authConfig.CredentialGroup;
        if (groupName is not null && _sharedCredentials.TryGetValue(groupName, out var credential))
        {
            ProxyLogger.InteractiveBrowserCredentialReused(_logger, groupName);
        }
        else
        {
            var credentialOptions = new InteractiveBrowserCredentialOptions
            {
                ClientId = config.ClientId,
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions
                {
                    Name = config.TokenCacheName ?? "mcp-proxy"
                }
            };

            if (!string.IsNullOrEmpty(config.TenantId))
            {
                credentialOptions.TenantId = config.TenantId;
            }

            if (!string.IsNullOrEmpty(config.RedirectUri))
            {
                credentialOptions.RedirectUri = new Uri(config.RedirectUri);
            }

            credential = new InteractiveBrowserCredential(credentialOptions);

            var effectiveTenant = config.TenantId ?? "organizations";
            ProxyLogger.InteractiveBrowserCredentialCreated(_logger, config.ClientId, effectiveTenant);

            // Store in shared cache if a credential group is specified
            if (groupName is not null)
            {
                _sharedCredentials[groupName] = credential;
            }
        }

        var handler = new DefaultAzureCredentialAuthHandler(
            scopes,
            _loggerFactory.CreateLogger<DefaultAzureCredentialAuthHandler>(),
            credential);

        return new HttpClient(handler);
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
                _healthTracker.RecordConnectionState(name, connected: false);
                ProxyLogger.BackendDisconnected(_logger, name);
            }
            catch (Exception ex)
            {
                _healthTracker.RecordConnectionState(name, connected: false);
                _logger.LogWarning(ex, "Error disposing client {ServerName}", name);
            }
        }

        // Dispose credential providers and other disposables
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        _disposables.Clear();
        _clients.Clear();
        _lock.Dispose();
    }
}
