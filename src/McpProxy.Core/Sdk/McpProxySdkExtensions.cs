using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Hooks;
using McpProxy.Core.Proxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace McpProxy.Core.Sdk;

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
        services.AddSingleton<ProxyClientHandlers>();
        services.AddSingleton<NotificationForwarder>();
        services.AddSingleton<McpClientManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<McpClientManager>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var proxyClientHandlers = sp.GetRequiredService<ProxyClientHandlers>();
            var notificationForwarder = sp.GetRequiredService<NotificationForwarder>();
            return new McpClientManager(logger, loggerFactory, proxyClientHandlers, notificationForwarder);
        });
        services.AddSingleton<HookFactory>();
        services.AddSingleton<SdkEnabledProxyServer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SdkEnabledProxyServer>>();
            var clientManager = sp.GetRequiredService<McpClientManager>();
            var sdkConfig = sp.GetRequiredService<McpProxySdkConfiguration>();
            var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
            return new SdkEnabledProxyServer(logger, clientManager, sdkConfig, null, httpContextAccessor);
        });

        // Add telemetry
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<ProxyConfiguration>();
            return config;
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
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ListToolsAsync(context, token);
            })
            .WithCallToolHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.CallToolAsync(context, token);
            })
            .WithListResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ListResourcesAsync(context, token);
            })
            .WithReadResourceHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ReadResourceAsync(context, token);
            })
            .WithListPromptsHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.ListPromptsAsync(context, token);
            })
            .WithGetPromptHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.GetPromptAsync(context, token);
            })
            .WithSubscribeToResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.SubscribeToResourceAsync(context, token);
            })
            .WithUnsubscribeFromResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<SdkEnabledProxyServer>();
                return proxy.UnsubscribeFromResourceAsync(context, token);
            });
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
        var clientManager = host.Services.GetRequiredService<McpClientManager>();
        var proxyServer = host.Services.GetRequiredService<SdkEnabledProxyServer>();
        var hookFactory = host.Services.GetRequiredService<HookFactory>();
        var pipelineLogger = host.Services.GetRequiredService<ILogger<HookPipeline>>();

        // Initialize backend connections
        await clientManager.InitializeAsync(sdkConfig.Configuration, cancellationToken).ConfigureAwait(false);

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
}
