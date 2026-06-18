using McpProxy.Abstractions;
using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Debugging;
using McpProxy.Sdk.Exceptions;
using McpProxy.Sdk.Logging;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<string, McpClientInfo> _clients = [];
    private readonly ConcurrentDictionary<string, ServerConfiguration> _deferredClients = [];
    private readonly ConcurrentDictionary<string, InteractiveBrowserCredential> _sharedCredentials = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BackendAuthenticationException> _lastAuthFailure = new(StringComparer.OrdinalIgnoreCase);
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
    /// Gets the most recent unresolved authentication failures, keyed by server name.
    /// An entry is added when a backend connection or listing fails due to an authentication
    /// error (for example, an expired interactive-browser credential) and removed when the
    /// backend subsequently connects successfully.
    /// </summary>
    public IReadOnlyCollection<BackendAuthenticationException> RecentAuthFailures => [.. _lastAuthFailure.Values];

    /// <summary>
    /// Gets the recorded authentication failure for a specific server, or <c>null</c> if none.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    public BackendAuthenticationException? GetAuthFailure(string serverName) =>
        _lastAuthFailure.TryGetValue(serverName, out var failure) ? failure : null;

    /// <summary>
    /// Records an authentication failure for a server so it can be surfaced to clients that
    /// later access the affected credential group.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="failure">The wrapped authentication failure.</param>
    public void RecordAuthFailure(string serverName, BackendAuthenticationException failure) =>
        _lastAuthFailure[serverName] = failure;

    /// <summary>
    /// Clears any recorded authentication failure for a server (e.g., after a successful connect).
    /// </summary>
    /// <param name="serverName">The server name.</param>
    public void ClearAuthFailure(string serverName) => _lastAuthFailure.TryRemove(serverName, out _);

    /// <summary>
    /// If <paramref name="exception"/> is an authentication failure, records a wrapped
    /// <see cref="BackendAuthenticationException"/> for the server and returns it; otherwise
    /// returns <c>null</c> and records nothing.
    /// </summary>
    private BackendAuthenticationException? TryRecordAuthFailure(string serverName, ServerConfiguration config, Exception exception)
    {
        if (!BackendAuthClassifier.IsAuthFailure(exception))
        {
            return null;
        }

        var wrapped = exception as BackendAuthenticationException
            ?? BackendAuthenticationException.From(serverName, config.Auth?.CredentialGroup, exception);
        _lastAuthFailure[serverName] = wrapped;
        ProxyLogger.InteractiveSignInRequired(_logger, serverName, config.Auth?.CredentialGroup ?? "(none)");
        return wrapped;
    }

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
                // Backends with DeferConnection also defer to avoid triggering interactive
                // authentication (e.g., browser sign-in) at startup time.
                if (serverConfig.Auth?.Type == BackendAuthType.ForwardAuthorization
                    || serverConfig.Auth?.DeferConnection == true)
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
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation — caller requested abort
                }
                catch (Exception ex)
                {
                    // Log and defer — the backend will be retried on first client request.
                    // This prevents startup crashes when a backend is temporarily unreachable
                    // or when credentials need interactive consent.
                    ProxyLogger.BackendConnectionFailed(_logger, name, ex);
                    _healthTracker.RecordConnectionState(name, connected: false);
                    _healthTracker.RecordFailure(name, ex.Message);
                    TryRecordAuthFailure(name, serverConfig, ex);
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
        if (_deferredClients.IsEmpty)
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
                    _deferredClients.TryRemove(name, out _);
                    continue;
                }

                var serverConfig = _deferredClients[name];
                try
                {
                    var client = await CreateClientAsync(name, serverConfig, cancellationToken).ConfigureAwait(false);
                    _clients[name] = client;
                    _deferredClients.TryRemove(name, out _);
                }
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation — caller requested abort
                }
                catch (Exception ex)
                {
                    // Log but do not throw — the backend likely requires authentication
                    // that isn't available yet. The client stays deferred and will be
                    // retried on subsequent requests once the caller authenticates.
                    // Auth failures are additionally recorded so an aggregated/unified
                    // proxy can surface them when the affected group is accessed.
                    ProxyLogger.BackendConnectionFailed(_logger, name, ex);
                    _healthTracker.RecordConnectionState(name, connected: false);
                    _healthTracker.RecordFailure(name, ex.Message);
                    TryRecordAuthFailure(name, serverConfig, ex);
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
    public bool HasDeferredClients => !_deferredClients.IsEmpty;

    /// <summary>
    /// Attempts to connect a specific deferred client by name. If the client is already
    /// connected this returns <c>true</c> immediately. If the name is not in the deferred
    /// list (either never deferred, or already removed) this returns <c>false</c>. If the
    /// name IS deferred and a connect attempt is made, the result is one of:
    /// <list type="bullet">
    ///   <item><c>true</c> — connect succeeded; the client is now registered in
    ///   <see cref="Clients"/> and removed from the deferred list.</item>
    ///   <item>An exception is thrown — connect failed. The original exception
    ///   (auth failure, transport error, etc.) propagates to the caller so the
    ///   underlying cause is visible to whatever protocol envelope the caller
    ///   serializes (JSON-RPC error for tools/list, <c>CallToolResult.IsError</c>
    ///   for tool calls, etc.). The deferred entry stays in place so the next
    ///   request retries the connect — a single failed attempt does not
    ///   permanently disable the backend.</item>
    /// </list>
    /// Cancellation is always re-thrown without touching the health tracker.
    /// Used by <see cref="SingleServerProxy"/> to lazily connect deferred backends on
    /// first request.
    /// </summary>
    /// <param name="serverName">The server name to connect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the client is now connected; <c>false</c> if the
    /// name was never deferred (and no connect attempt was made).</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<bool> EnsureDeferredClientConnectedAsync(string serverName, CancellationToken cancellationToken = default)
    {
        // Already connected
        if (_clients.ContainsKey(serverName))
        {
            return true;
        }

        // Not deferred
        if (!_deferredClients.ContainsKey(serverName))
        {
            return false;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_clients.ContainsKey(serverName))
            {
                _deferredClients.TryRemove(serverName, out _);
                return true;
            }

            if (!_deferredClients.TryGetValue(serverName, out var serverConfig))
            {
                return false;
            }

            try
            {
                var client = await CreateClientAsync(serverName, serverConfig, cancellationToken).ConfigureAwait(false);
                _clients[serverName] = client;
                _deferredClients.TryRemove(serverName, out _);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw; // Propagate cancellation — caller requested abort
            }
            catch (Exception ex)
            {
                // Log the structured error for proxy operators…
                ProxyLogger.BackendConnectionFailed(_logger, serverName, ex);

                // …and surface it on the health tracker so the failure shows up
                // in /debug/health (and any downstream consumer of IHealthTracker).
                _healthTracker.RecordConnectionState(serverName, connected: false);
                _healthTracker.RecordFailure(serverName, ex.Message);

                // Record auth failures so consumers can present an actionable message.
                TryRecordAuthFailure(serverName, serverConfig, ex);

                // Re-throw the original exception so callers (SingleServerProxy
                // and its consumers) see the actual underlying cause —
                // historically this catch returned `false`, which made
                // GetClientInfoAsync return null and made ListToolsAsync /
                // CallToolAsync produce silent empty/"not available" responses.
                // That hid auth failures (token expiry, missing consent,
                // network errors, etc.) behind a generic "0 tools" symptom.
                // Re-throwing lets each upstream caller decide how to surface
                // the error in its own protocol envelope (CallToolResult.IsError,
                // JSON-RPC error for tools/list, etc.).
                //
                // The deferred entry is intentionally NOT removed from
                // _deferredClients so the next request will retry the connect —
                // a single failed attempt (e.g. transient network blip, expired
                // token that will be refreshed via cached refresh-token) should
                // not permanently disable the backend.
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the names of backends that are still deferred (not yet connected).
    /// </summary>
    public IReadOnlyCollection<string> DeferredClientNames => [.. _deferredClients.Keys];

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

        // Monitor for unexpected backend disconnection
        MonitorClientCompletion(client, name, config);

        ProxyLogger.ConnectedToBackend(_logger, name);

        // Record connection state in health tracker
        _healthTracker.RecordConnectionState(name, connected: true);

        // A successful connection clears any previously recorded auth failure.
        ClearAuthFailure(name);

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
    /// Monitors a backend client for unexpected disconnection via its <see cref="McpClient.Completion"/> task.
    /// When the backend session ends (e.g., process crash, network failure, token expiry that drops the
    /// stream), the health tracker is updated, a warning is logged, and the backend is re-armed for lazy
    /// reconnection: it is removed from the connected set and placed back in the deferred set so the next
    /// request re-establishes the connection (re-running interactive sign-in if the credential expired).
    /// This runs as a fire-and-forget continuation so it does not block the connection path.
    /// </summary>
    private void MonitorClientCompletion(McpClient client, string serverName, ServerConfiguration config)
    {
        _ = client.Completion.ContinueWith(completionTask =>
        {
            _healthTracker.RecordConnectionState(serverName, connected: false);

            if (completionTask.IsCompletedSuccessfully)
            {
                var details = completionTask.Result;
                if (details.Exception is not null)
                {
                    ProxyLogger.BackendDisconnectedUnexpectedly(_logger, serverName, details.Exception);
                }
                else
                {
                    ProxyLogger.BackendDisconnected(_logger, serverName);
                }
            }
            else
            {
                ProxyLogger.BackendDisconnected(_logger, serverName);
            }

            ReArmDisconnectedClient(serverName, config);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Removes a dropped client from the connected set and returns it to the deferred set so the
    /// next request reconnects lazily. No-op during disposal.
    /// </summary>
    private void ReArmDisconnectedClient(string serverName, ServerConfiguration config)
    {
        if (_disposed)
        {
            return;
        }

        // Drop the dead client. The next access goes through the deferred-connect path,
        // which re-runs CreateClientAsync (and therefore re-authentication if required).
        _clients.TryRemove(serverName, out _);
        _deferredClients[serverName] = config;
        ProxyLogger.BackendReArmedAfterDisconnect(_logger, serverName);
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
        // Use the explicit transport mode matching the configured type:
        // - Sse → SSE only (legacy servers)
        // - Http → Streamable HTTP only (modern servers)
        // AutoDetect is not used because its SSE fallback path can hang
        // indefinitely against pure Streamable HTTP backends that accept
        // the SSE GET request but never emit SSE events.
        var transportMode = config.Type == ServerTransportType.Sse
            ? HttpTransportMode.Sse
            : HttpTransportMode.StreamableHttp;

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

        // Dispose backend clients in parallel to minimize shutdown latency when
        // multiple backends are connected. Each disposal is wrapped in its own
        // try/catch so a single failing backend does not prevent the others
        // from being disposed.
        var disposeTasks = _clients.Select(kvp => DisposeClientAsync(kvp.Key, kvp.Value));
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);

        // Dispose credential providers and other disposables
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                ProxyLogger.DisposableCleanupFailed(_logger, ex);
            }
        }

        _disposables.Clear();
        _clients.Clear();
        _lock.Dispose();
    }

    private async Task DisposeClientAsync(string name, McpClientInfo clientInfo)
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
}
