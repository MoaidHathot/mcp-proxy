using McpProxy.Abstractions;
using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Debugging;
using McpProxy.Sdk.Hooks;
using McpProxy.Sdk.Logging;
using McpProxy.Sdk.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProxy.Sdk.Sdk;

/// <summary>
/// Extension methods for integrating MCP Proxy SDK with dependency injection.
/// </summary>
public static class McpProxySdkExtensions
{
    /// <summary>
    /// Adds MCP Proxy services using SDK configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure the proxy builder.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMcpProxy(
        this IServiceCollection services,
        Action<IMcpProxyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = McpProxyBuilder.Create();
        configure(builder);
        var sdkConfig = builder.BuildConfiguration();

        return AddMcpProxyCore(services, sdkConfig);
    }

    /// <summary>
    /// Adds MCP Proxy services using SDK configuration with async initialization support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Async action to configure the proxy builder.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMcpProxy(
        this IServiceCollection services,
        Func<IMcpProxyBuilder, Task> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Store the configure action to be executed during initialization
        services.AddSingleton<McpProxySdkConfigurationProvider>(sp =>
        {
            var builder = McpProxyBuilder.Create();
            configure(builder).GetAwaiter().GetResult();
            return new McpProxySdkConfigurationProvider(builder.BuildConfiguration());
        });

        return AddMcpProxyServicesDeferred(services);
    }

    /// <summary>
    /// Adds MCP Proxy services using a pre-built SDK configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sdkConfig">The SDK configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMcpProxy(
        this IServiceCollection services,
        McpProxySdkConfiguration sdkConfig)
    {
        ArgumentNullException.ThrowIfNull(sdkConfig);
        return AddMcpProxyCore(services, sdkConfig);
    }

    private static IServiceCollection AddMcpProxyCore(
        IServiceCollection services,
        McpProxySdkConfiguration sdkConfig)
    {
        services.AddSingleton(sdkConfig);
        services.AddSingleton(sdkConfig.Configuration);
        services.AddSingleton<McpProxySdkConfigurationProvider>(_ => 
            new McpProxySdkConfigurationProvider(sdkConfig));

        return RegisterSdkServices(services);
    }

    private static IServiceCollection AddMcpProxyServicesDeferred(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var provider = sp.GetRequiredService<McpProxySdkConfigurationProvider>();
            return provider.Configuration;
        });

        services.AddSingleton(sp =>
        {
            var sdkConfig = sp.GetRequiredService<McpProxySdkConfiguration>();
            return sdkConfig.Configuration;
        });

        return RegisterSdkServices(services);
    }

    private static IServiceCollection RegisterSdkServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient(); // Required for OAuth metadata probing

        // Register null debugging defaults — SDK consumers can override with real implementations
        services.TryAddSingleton<IHealthTracker>(NullHealthTracker.Instance);
        services.TryAddSingleton<IHookTracer>(NullHookTracer.Instance);
        services.TryAddSingleton<IRequestDumper>(NullRequestDumper.Instance);

        services.AddSingleton<ProxyClientHandlers>();
        services.AddSingleton<NotificationForwarder>();
        services.AddSingleton<McpClientManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<McpClientManager>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var proxyClientHandlers = sp.GetRequiredService<ProxyClientHandlers>();
            var notificationForwarder = sp.GetRequiredService<NotificationForwarder>();
            var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
            var healthTracker = sp.GetService<IHealthTracker>();
            return new McpClientManager(logger, loggerFactory, proxyClientHandlers, notificationForwarder, healthTracker, httpContextAccessor);
        });
        services.AddSingleton<HookFactory>();
        services.AddSingleton<SdkEnabledProxyServer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SdkEnabledProxyServer>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var clientManager = sp.GetRequiredService<McpClientManager>();
            var sdkConfig = sp.GetRequiredService<McpProxySdkConfiguration>();
            var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
            return new SdkEnabledProxyServer(logger, loggerFactory, clientManager, sdkConfig, null, httpContextAccessor);
        });

        // Add OAuth metadata services
        services.AddSingleton<IOAuthMetadataRegistry, OAuthMetadataRegistry>();
        services.AddSingleton<IOAuthMetadataProbe, OAuthMetadataProbe>();

        // Register SingleServerProxy instances for PerServer routing mode.
        // Each enabled server gets a keyed singleton so that per-server endpoints
        // resolve only the tools from their own backend.
        services.AddSingleton<IPerServerProxyRegistrar>(sp =>
        {
            var config = sp.GetRequiredService<ProxyConfiguration>();
            if (config.Proxy.Routing.Mode != RoutingMode.PerServer)
            {
                return new NoOpPerServerProxyRegistrar();
            }

            var sdkConfig = sp.GetRequiredService<McpProxySdkConfiguration>();
            var logger = sp.GetRequiredService<ILogger<SingleServerProxy>>();
            var clientManager = sp.GetRequiredService<McpClientManager>();
            var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase);
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase);

            // Collect global virtual tools if configured to show on per-server routes
            var globalVirtualTools = sdkConfig.ShowGlobalVirtualToolsOnPerServerRoutes
                ? sdkConfig.VirtualTools
                : [];

            foreach (var (serverName, serverConfig) in config.Mcp.Where(m => m.Value.Enabled))
            {
                // Merge per-server virtual tools with global virtual tools (if flag is set)
                var serverVirtualTools = new List<VirtualToolDefinition>(globalVirtualTools);
                if (sdkConfig.ServerStates.TryGetValue(serverName, out var serverState))
                {
                    serverVirtualTools.AddRange(serverState.VirtualTools);
                }

                var proxy = new SingleServerProxy(
                    logger, clientManager, serverName, serverConfig, httpContextAccessor, serverVirtualTools);
                proxies[serverName] = proxy;

                var route = serverConfig.Route ?? $"{config.Proxy.Routing.BasePath}/{serverName}";
                routeToProxy[route] = proxy;
            }

            return new PerServerProxyRegistrar(proxies, routeToProxy);
        });

        return services;
    }

    /// <summary>
    /// Configures MCP Server with SDK proxy handlers.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithSdkProxyHandlers(this IMcpServerBuilder builder)
    {
        return builder
            .WithListToolsHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    return singleProxy.ListToolsAsync(token);
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ListToolsAsync(context, token);
            })
            .WithCallToolHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    return singleProxy.CallToolAsync(context.Params!, token);
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.CallToolAsync(context, token);
            })
            .WithListResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    return singleProxy.ListResourcesAsync(token);
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ListResourcesAsync(context, token);
            })
            .WithReadResourceHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    return singleProxy.ReadResourceAsync(context.Params!, token);
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ReadResourceAsync(context, token);
            })
            .WithListPromptsHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    return singleProxy.ListPromptsAsync(token);
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ListPromptsAsync(context, token);
            })
            .WithGetPromptHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    return singleProxy.GetPromptAsync(context.Params!, token);
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.GetPromptAsync(context, token);
            })
            .WithSubscribeToResourcesHandler(async (context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    await singleProxy.SubscribeToResourceAsync(context.Params!.Uri, token).ConfigureAwait(false);
                    return new EmptyResult();
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return await proxy.SubscribeToResourceAsync(context, token).ConfigureAwait(false);
            })
            .WithUnsubscribeFromResourcesHandler(async (context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);

                if (TryGetPerServerProxy(context) is { } singleProxy)
                {
                    await singleProxy.UnsubscribeFromResourceAsync(context.Params!.Uri, token).ConfigureAwait(false);
                    return new EmptyResult();
                }

                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return await proxy.UnsubscribeFromResourceAsync(context, token).ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Checks if the current HTTP request matches a per-server route and returns
    /// the corresponding <see cref="SingleServerProxy"/>. Returns <c>null</c> for
    /// unified endpoint requests, causing the caller to fall through to <see cref="SdkEnabledProxyServer"/>.
    /// </summary>
    private static SingleServerProxy? TryGetPerServerProxy<TParams>(RequestContext<TParams> context) where TParams : class
    {
        var httpContextAccessor = context.Server!.Services!.GetService<IHttpContextAccessor>();
        var httpContext = httpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        var registrar = context.Server.Services!.GetService<IPerServerProxyRegistrar>();
        return registrar?.TryGetProxyForRoute(httpContext.Request.Path);
    }

    private static void EnsureProxyClientHandlersInitialized<TParams>(RequestContext<TParams> context) where TParams : class
    {
        var handlers = context.Server!.Services!.GetRequiredService<ProxyClientHandlers>();
        handlers.SetMcpServer(context.Server);

        var notificationForwarder = context.Server!.Services!.GetRequiredService<NotificationForwarder>();
        notificationForwarder.SetMcpServer(context.Server);
    }
}

/// <summary>
/// Provides the SDK configuration for dependency injection.
/// </summary>
public sealed class McpProxySdkConfigurationProvider
{
    /// <summary>
    /// Gets the SDK configuration.
    /// </summary>
    public McpProxySdkConfiguration Configuration { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="configuration">The SDK configuration.</param>
    public McpProxySdkConfigurationProvider(McpProxySdkConfiguration configuration)
    {
        Configuration = configuration;
    }
}

/// <summary>
/// Extension methods for initializing the MCP Proxy SDK.
/// </summary>
public static class McpProxySdkHostExtensions
{
    /// <summary>
    /// Initializes the MCP Proxy SDK, connecting to backend servers.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the initialization.</returns>
    public static async Task InitializeMcpProxyAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        var sdkConfig = host.Services.GetRequiredService<McpProxySdkConfiguration>();

        // Load and merge configuration file if specified (SDK config takes priority)
        if (!string.IsNullOrEmpty(sdkConfig.ConfigFilePath))
        {
            var fileConfig = await ConfigurationLoader.LoadAsync(sdkConfig.ConfigFilePath, cancellationToken).ConfigureAwait(false);

            // Merge file servers into SDK config — SDK-defined servers take priority
            foreach (var (name, serverConfig) in fileConfig.Mcp)
            {
                sdkConfig.Configuration.Mcp.TryAdd(name, serverConfig);
            }
        }

        var clientManager = host.Services.GetRequiredService<McpClientManager>();
        var proxyServer = host.Services.GetRequiredService<SdkEnabledProxyServer>();
        var hookFactory = host.Services.GetRequiredService<HookFactory>();
        var pipelineLogger = host.Services.GetRequiredService<ILogger<HookPipeline>>();

        // Initialize backend connections
        await clientManager.InitializeAsync(sdkConfig.Configuration, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Probe backends with ForwardAuthorization for OAuth metadata support
        await ProbeOAuthBackendsAsync(host.Services, sdkConfig.Configuration, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Configure hook pipelines from SDK configuration
        foreach (var (serverName, serverState) in sdkConfig.ServerStates)
        {
            if (serverState.PreInvokeHooks.Count == 0 && serverState.PostInvokeHooks.Count == 0)
            {
                // Check if there are hooks from JSON configuration
                if (!sdkConfig.Configuration.Mcp.TryGetValue(serverName, out var serverConfig))
                {
                    continue;
                }

                if (serverConfig.Hooks.PreInvoke is null or { Length: 0 } &&
                    serverConfig.Hooks.PostInvoke is null or { Length: 0 })
                {
                    continue;
                }

                // Configure from JSON
                var jsonPipeline = new HookPipeline(pipelineLogger);
                hookFactory.ConfigurePipeline(serverConfig.Hooks, jsonPipeline);
                proxyServer.AddHookPipeline(serverName, jsonPipeline);
            }
            else
            {
                // Configure from SDK
                var pipeline = new HookPipeline(pipelineLogger);
                
                foreach (var hook in serverState.PreInvokeHooks)
                {
                    pipeline.AddPreInvokeHook(hook);
                }

                foreach (var hook in serverState.PostInvokeHooks)
                {
                    pipeline.AddPostInvokeHook(hook);
                }

                proxyServer.AddHookPipeline(serverName, pipeline);
            }
        }

        // Configure hook pipelines for SingleServerProxy instances (PerServer routing mode)
        var perServerRegistrar = host.Services.GetService<IPerServerProxyRegistrar>();
        if (perServerRegistrar is { Proxies.Count: > 0 })
        {
            foreach (var (serverName, singleServerProxy) in perServerRegistrar.Proxies)
            {
                if (!sdkConfig.Configuration.Mcp.TryGetValue(serverName, out var serverConfig))
                {
                    continue;
                }

                // Check SDK hooks first, then JSON hooks
                if (sdkConfig.ServerStates.TryGetValue(serverName, out var serverState) &&
                    (serverState.PreInvokeHooks.Count > 0 || serverState.PostInvokeHooks.Count > 0))
                {
                    var pipeline = new HookPipeline(pipelineLogger);

                    foreach (var hook in serverState.PreInvokeHooks)
                    {
                        pipeline.AddPreInvokeHook(hook);
                    }

                    foreach (var hook in serverState.PostInvokeHooks)
                    {
                        pipeline.AddPostInvokeHook(hook);
                    }

                    singleServerProxy.SetHookPipeline(pipeline);
                }
                else if (serverConfig.Hooks.PreInvoke is { Length: > 0 } ||
                         serverConfig.Hooks.PostInvoke is { Length: > 0 })
                {
                    var pipeline = new HookPipeline(pipelineLogger);
                    hookFactory.ConfigurePipeline(serverConfig.Hooks, pipeline);
                    singleServerProxy.SetHookPipeline(pipeline);
                }
            }
        }
    }

    private static async Task ProbeOAuthBackendsAsync(
        IServiceProvider services,
        ProxyConfiguration config,
        CancellationToken cancellationToken)
    {
        var probe = services.GetService<IOAuthMetadataProbe>();
        var registry = services.GetService<IOAuthMetadataRegistry>();
        var logger = services.GetService<ILogger<OAuthMetadataProbe>>();

        if (probe is null || registry is null)
        {
            return;
        }

        // Find all SSE backends with ForwardAuthorization
        var probeTasks = new List<(string ServerName, string BackendUrl, Task<OAuthProbeResult> ProbeTask)>();

        foreach (var (serverName, serverConfig) in config.Mcp)
        {
            if (serverConfig.Type != ServerTransportType.Sse && serverConfig.Type != ServerTransportType.Http)
            {
                continue;
            }

            if (serverConfig.Auth?.Type != BackendAuthType.ForwardAuthorization)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(serverConfig.Url))
            {
                continue;
            }

            probeTasks.Add((serverName, serverConfig.Url, probe.ProbeAsync(serverConfig.Url, cancellationToken)));
        }

        if (probeTasks.Count == 0)
        {
            return;
        }

        // Wait for all probes to complete
        await Task.WhenAll(probeTasks.Select(p => p.ProbeTask)).ConfigureAwait(false);

        // Register backends that support OAuth
        foreach (var (serverName, backendUrl, probeTask) in probeTasks)
        {
            var result = await probeTask.ConfigureAwait(false);
            if (result.SupportsOAuth)
            {
                registry.Register(serverName, backendUrl, result);

                if (logger is not null)
                {
                    ProxyLogger.OAuthMetadataAutoConfigured(logger, serverName, backendUrl);
                }
            }
        }
    }

    /// <summary>
    /// Initializes the MCP Proxy SDK for a WebApplication.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the initialization.</returns>
    public static async Task InitializeMcpProxyAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await ((IHost)app).InitializeMcpProxyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds OAuth metadata proxy middleware that automatically serves OAuth metadata from backends.
    /// This should be called before MapMcp() in the middleware pipeline.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="cacheDuration">How long to cache OAuth metadata. Default is 15 minutes.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseOAuthMetadataProxy(this WebApplication app, TimeSpan? cacheDuration = null)
    {
        app.UseMiddleware<OAuthMetadataProxyMiddleware>(cacheDuration ?? TimeSpan.FromMinutes(15));
        return app;
    }

    /// <summary>
    /// Adds authentication middleware that requires a Bearer token to be present on
    /// incoming requests without validating it. The token is forwarded to the backend
    /// which performs the actual validation. If no token is present, the middleware
    /// returns a 401 challenge with RFC 9728 resource_metadata hints so the MCP client
    /// can trigger its OAuth flow.
    /// </summary>
    /// <remarks>
    /// This should be called after <see cref="UseOAuthMetadataProxy"/> and before
    /// <c>MapMcp()</c> in the middleware pipeline. The OAuth metadata middleware must
    /// come first so that the <c>/.well-known/</c> discovery endpoints are accessible
    /// without authentication.
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseForwardAuthAuthentication(this WebApplication app)
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Type = AuthenticationType.ForwardAuthorization
        };

        app.UseMcpProxyAuthentication(config);
        return app;
    }

    /// <summary>
    /// Configures Azure AD backend authentication for this server (HTTP/SSE only).
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="authType">The Azure AD authentication type.</param>
    /// <param name="configure">Action to configure Azure AD settings.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder WithBackendAuth(
        this IServerBuilder builder,
        BackendAuthType authType,
        Action<BackendAzureAdConfiguration> configure)
    {
        if (builder is ServerBuilder serverBuilder)
        {
            return serverBuilder.WithBackendAuth(authType, configure);
        }

        throw new InvalidOperationException(
            $"WithBackendAuth is only supported on {nameof(ServerBuilder)}. " +
            $"Actual type: {builder.GetType().Name}");
    }

    /// <summary>
    /// Maps per-server MCP endpoints for PerServer routing mode.
    /// Each enabled backend server gets its own set of endpoints at
    /// <c>{basePath}/{serverName}</c> (or the server's custom <c>route</c>).
    /// Tools, resources, and prompts on each endpoint are isolated to that server's backend.
    /// </summary>
    /// <remarks>
    /// This should be called after <see cref="McpProxySdkHostExtensions.InitializeMcpProxyAsync(WebApplication, CancellationToken)"/>
    /// to ensure backend connections and hook pipelines are configured.
    /// A unified MCP endpoint is still available at <c>{basePath}</c> which aggregates all servers.
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapPerServerMcpEndpoints(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<ProxyConfiguration>();

        if (config.Proxy.Routing.Mode != RoutingMode.PerServer)
        {
            return app;
        }

        var registrar = app.Services.GetRequiredService<IPerServerProxyRegistrar>();
        var logger = app.Services.GetRequiredService<ILogger<SingleServerProxy>>();

        foreach (var (serverName, _) in registrar.Proxies)
        {
            var serverConfig = config.Mcp[serverName];
            var route = serverConfig.Route ?? $"{config.Proxy.Routing.BasePath}/{serverName}";

            // Map MCP Streamable HTTP endpoint for this server.
            // The proxy handlers in WithSdkProxyHandlers() detect the per-server
            // route via IPerServerProxyRegistrar.TryGetProxyForRoute() and delegate
            // to SingleServerProxy instead of SdkEnabledProxyServer, providing
            // per-server tool/resource/prompt isolation over the MCP protocol.
            app.MapMcp(route)
                .ApplyServerCorsPolicy(config, serverName);

            // Also map REST sub-routes for backward compatibility
            MapSingleServerEndpoint(app, serverName, route, registrar);
            ProxyLogger.RegisteredServerEndpoint(logger, serverName, route);
        }

        return app;
    }

    private static void MapSingleServerEndpoint(
        WebApplication app,
        string serverName,
        string route,
        IPerServerProxyRegistrar registrar)
    {
        app.MapPost($"{route}/tools/list", async (HttpContext context) =>
        {
            var proxy = registrar.GetProxy(serverName);
            var result = await proxy.ListToolsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result);
        });

        app.MapPost($"{route}/tools/call", async (HttpContext context, CallToolRequestParams request) =>
        {
            var proxy = registrar.GetProxy(serverName);
            var result = await proxy.CallToolAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result);
        });

        app.MapPost($"{route}/resources/list", async (HttpContext context) =>
        {
            var proxy = registrar.GetProxy(serverName);
            var result = await proxy.ListResourcesAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result);
        });

        app.MapPost($"{route}/resources/read", async (HttpContext context, ReadResourceRequestParams request) =>
        {
            var proxy = registrar.GetProxy(serverName);
            var result = await proxy.ReadResourceAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result);
        });

        app.MapPost($"{route}/prompts/list", async (HttpContext context) =>
        {
            var proxy = registrar.GetProxy(serverName);
            var result = await proxy.ListPromptsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result);
        });

        app.MapPost($"{route}/prompts/get", async (HttpContext context, GetPromptRequestParams request) =>
        {
            var proxy = registrar.GetProxy(serverName);
            var result = await proxy.GetPromptAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result);
        });
    }
}

/// <summary>
/// Registry for per-server proxy instances. Used internally by the SDK to manage
/// <see cref="SingleServerProxy"/> instances for PerServer routing mode.
/// </summary>
public interface IPerServerProxyRegistrar
{
    /// <summary>
    /// Gets all registered per-server proxies.
    /// </summary>
    IReadOnlyDictionary<string, SingleServerProxy> Proxies { get; }

    /// <summary>
    /// Gets a per-server proxy by server name.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>The proxy for the specified server.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if no proxy is registered for the server name.</exception>
    SingleServerProxy GetProxy(string serverName);

    /// <summary>
    /// Gets the per-server proxy for a given HTTP request path, if any.
    /// Returns <c>null</c> if the path does not match a per-server route (i.e., it's the unified endpoint).
    /// </summary>
    /// <param name="path">The HTTP request path (e.g., "/mcp/server-a").</param>
    /// <returns>The matching proxy, or <c>null</c>.</returns>
    SingleServerProxy? TryGetProxyForRoute(string path);
}

/// <summary>
/// Holds the registered <see cref="SingleServerProxy"/> instances for PerServer routing mode.
/// </summary>
public sealed class PerServerProxyRegistrar : IPerServerProxyRegistrar
{
    private readonly Dictionary<string, SingleServerProxy> _proxies;
    private readonly Dictionary<string, SingleServerProxy> _routeToProxy;

    public PerServerProxyRegistrar(
        Dictionary<string, SingleServerProxy> proxies,
        Dictionary<string, SingleServerProxy> routeToProxy)
    {
        _proxies = proxies;
        _routeToProxy = routeToProxy;
    }

    public IReadOnlyDictionary<string, SingleServerProxy> Proxies => _proxies;

    public SingleServerProxy GetProxy(string serverName)
    {
        if (_proxies.TryGetValue(serverName, out var proxy))
        {
            return proxy;
        }

        throw new KeyNotFoundException($"No per-server proxy registered for server '{serverName}'. " +
            $"Available servers: {string.Join(", ", _proxies.Keys)}");
    }

    public SingleServerProxy? TryGetProxyForRoute(string path)
    {
        foreach (var (route, proxy) in _routeToProxy)
        {
            // Match if the path equals the route or starts with the route followed by a separator
            if (path.Equals(route, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase))
            {
                return proxy;
            }
        }

        return null;
    }
}

/// <summary>
/// No-op registrar used when PerServer routing mode is not active.
/// </summary>
public sealed class NoOpPerServerProxyRegistrar : IPerServerProxyRegistrar
{
    public IReadOnlyDictionary<string, SingleServerProxy> Proxies { get; } =
        new Dictionary<string, SingleServerProxy>();

    public SingleServerProxy GetProxy(string serverName)
    {
        throw new InvalidOperationException(
            "Per-server routing is not enabled. Set routing.mode to 'perServer' in the proxy configuration.");
    }

    public SingleServerProxy? TryGetProxyForRoute(string path) => null;
}
