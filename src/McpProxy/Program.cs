using System.CommandLine;
using McpProxy.Core.Authentication;
using McpProxy.Core.Configuration;
using McpProxy.Core.Hooks;
using McpProxy.Core.Logging;
using McpProxy.Core.Proxy;
using McpProxy.Core.Telemetry;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var transportOption = new Option<TransportType>("--transport", "-t")
{
    Description = "Server transport type",
    DefaultValueFactory = _ => TransportType.Stdio
};

var configOption = new Option<string?>("--config", "-c")
{
    Description = "Path to mcp-proxy.json configuration file"
};

var portOption = new Option<int>("--port", "-p")
{
    Description = "Port for HTTP/SSE server (default: 5000)",
    DefaultValueFactory = _ => 5000
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Enable verbose logging",
    DefaultValueFactory = _ => false
};

var rootCommand = new RootCommand("MCP Proxy - Aggregates multiple MCP servers into a single endpoint");
rootCommand.Options.Add(transportOption);
rootCommand.Options.Add(configOption);
rootCommand.Options.Add(portOption);
rootCommand.Options.Add(verboseOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var transport = parseResult.GetValue(transportOption);
    var configPath = parseResult.GetValue(configOption);
    var port = parseResult.GetValue(portOption);
    var verbose = parseResult.GetValue(verboseOption);
    await RunProxyAsync(transport, configPath, port, verbose, cancellationToken).ConfigureAwait(false);
});

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

async Task RunProxyAsync(TransportType transport, string? configPath, int port, bool verbose, CancellationToken cancellationToken)
{
    // Resolve config path
    configPath ??= Environment.GetEnvironmentVariable("MCP_PROXY_CONFIG_PATH");

    if (string.IsNullOrEmpty(configPath))
    {
        // Try default locations
        var defaultPaths = new[] { "mcp-proxy.json", "config/mcp-proxy.json" };
        configPath = defaultPaths.FirstOrDefault(File.Exists);
    }

    if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
    {
        Console.Error.WriteLine("Error: Configuration file not found.");
        Console.Error.WriteLine("Provide config path via --config option or MCP_PROXY_CONFIG_PATH environment variable.");
        Environment.Exit(1);
        return;
    }

    // Load configuration
    var configuration = await ConfigurationLoader.LoadAsync(configPath, cancellationToken).ConfigureAwait(false);

    if (transport == TransportType.Stdio)
    {
        await RunStdioServerAsync(configuration, verbose).ConfigureAwait(false);
    }
    else
    {
        await RunHttpServerAsync(configuration, port, verbose).ConfigureAwait(false);
    }
}

async Task RunStdioServerAsync(ProxyConfiguration configuration, bool verbose)
{
    var builder = Host.CreateApplicationBuilder();
    ConfigureLogging(builder.Services, verbose);
    RegisterCoreServices(builder.Services, configuration);

    // Configure MCP Server with STDIO transport and all handlers
    builder.Services
        .AddMcpServer(options => ConfigureServerOptions(options, configuration))
        .WithStdioServerTransport()
        .WithProxyHandlers();

    var host = builder.Build();

    // Initialize backend connections and configure hooks
    var clientManager = host.Services.GetRequiredService<McpClientManager>();
    var proxyClientHandlers = host.Services.GetRequiredService<ProxyClientHandlers>();
    var proxyServer = host.Services.GetRequiredService<McpProxyServer>();
    var hookFactory = host.Services.GetRequiredService<HookFactory>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var pipelineLogger = host.Services.GetRequiredService<ILogger<HookPipeline>>();

    ProxyLogger.ProxyStarting(logger, "stdio");
    await clientManager.InitializeAsync(configuration).ConfigureAwait(false);
    
    // Configure hook pipelines from configuration
    ConfigureHookPipelines(configuration, proxyServer, hookFactory, pipelineLogger);
    
    ProxyLogger.ProxyStarted(logger, "stdio");

    // Note: The McpServer instance will be set on ProxyClientHandlers when the first request comes in
    // via the request context. For STDIO transport, there's typically only one client session.

    await host.RunAsync().ConfigureAwait(false);
}

async Task RunHttpServerAsync(ProxyConfiguration configuration, int port, bool verbose)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls($"http://localhost:{port}");
    ConfigureLogging(builder.Services, verbose);
    RegisterCoreServices(builder.Services, configuration);

    // Register SingleServerProxy instances for PerServer mode
    if (configuration.Proxy.Routing.Mode == RoutingMode.PerServer)
    {
        foreach (var (serverName, serverConfig) in configuration.Mcp.Where(m => m.Value.Enabled))
        {
            builder.Services.AddKeyedSingleton(serverName, (sp, _) =>
            {
                var logger = sp.GetRequiredService<ILogger<SingleServerProxy>>();
                var clientManager = sp.GetRequiredService<McpClientManager>();
                return new SingleServerProxy(logger, clientManager, serverName, serverConfig);
            });
        }
    }

    // Configure MCP Server with HTTP transport and all handlers
    builder.Services
        .AddMcpServer(options => ConfigureServerOptions(options, configuration))
        .WithHttpTransport()
        .WithProxyHandlers();

    var app = builder.Build();

    // Initialize backend connections and configure hooks
    var clientManager = app.Services.GetRequiredService<McpClientManager>();
    var proxyClientHandlers = app.Services.GetRequiredService<ProxyClientHandlers>();
    var proxyServer = app.Services.GetRequiredService<McpProxyServer>();
    var hookFactory = app.Services.GetRequiredService<HookFactory>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var pipelineLogger = app.Services.GetRequiredService<ILogger<HookPipeline>>();

    var url = $"http://localhost:{port}";
    ProxyLogger.ProxyStarting(logger, "http");
    await clientManager.InitializeAsync(configuration).ConfigureAwait(false);
    
    // Configure hook pipelines from configuration
    ConfigureHookPipelines(configuration, proxyServer, hookFactory, pipelineLogger);

    // Configure SingleServerProxy hook pipelines for PerServer mode
    if (configuration.Proxy.Routing.Mode == RoutingMode.PerServer)
    {
        foreach (var (serverName, serverConfig) in configuration.Mcp.Where(m => m.Value.Enabled))
        {
            var singleServerProxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
            ConfigureSingleServerHookPipeline(serverName, serverConfig, singleServerProxy, hookFactory, pipelineLogger);
        }
    }
    
    ProxyLogger.ProxyStarted(logger, url);

    // Map MCP endpoints based on routing mode
    if (configuration.Proxy.Routing.Mode == RoutingMode.PerServer)
    {
        // PerServer mode: Each server gets its own endpoint
        // Map to base path for unified endpoint (still available for discovery)
        app.MapMcp(configuration.Proxy.Routing.BasePath);
        
        // Map individual server endpoints
        foreach (var (serverName, serverConfig) in configuration.Mcp.Where(m => m.Value.Enabled))
        {
            var route = serverConfig.Route ?? $"{configuration.Proxy.Routing.BasePath}/{serverName}";
            MapSingleServerEndpoint(app, serverName, route);
            ProxyLogger.RegisteredServerEndpoint(logger, serverName, route);
        }
    }
    else
    {
        // Unified mode: All servers on one endpoint
        app.MapMcp(configuration.Proxy.Routing.BasePath);
    }

    // Map OAuth metadata discovery endpoints (RFC 8414) for MCP authentication
    app.MapOAuthMetadata(configuration.Proxy.Authentication);

    await app.RunAsync().ConfigureAwait(false);
}

void MapSingleServerEndpoint(WebApplication app, string serverName, string route)
{
    // Create MCP-like endpoints for single server
    // These endpoints allow direct access to a specific backend server
    app.MapPost($"{route}/tools/list", async (HttpContext context) =>
    {
        var proxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
        var result = await proxy.ListToolsAsync(context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
    
    app.MapPost($"{route}/tools/call", async (HttpContext context, ModelContextProtocol.Protocol.CallToolRequestParams request) =>
    {
        var proxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
        var result = await proxy.CallToolAsync(request, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
    
    app.MapPost($"{route}/resources/list", async (HttpContext context) =>
    {
        var proxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
        var result = await proxy.ListResourcesAsync(context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
    
    app.MapPost($"{route}/resources/read", async (HttpContext context, ModelContextProtocol.Protocol.ReadResourceRequestParams request) =>
    {
        var proxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
        var result = await proxy.ReadResourceAsync(request, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
    
    app.MapPost($"{route}/prompts/list", async (HttpContext context) =>
    {
        var proxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
        var result = await proxy.ListPromptsAsync(context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
    
    app.MapPost($"{route}/prompts/get", async (HttpContext context, ModelContextProtocol.Protocol.GetPromptRequestParams request) =>
    {
        var proxy = app.Services.GetRequiredKeyedService<SingleServerProxy>(serverName);
        var result = await proxy.GetPromptAsync(request, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
}

void ConfigureSingleServerHookPipeline(
    string serverName,
    ServerConfiguration serverConfig,
    SingleServerProxy proxy,
    HookFactory hookFactory,
    ILogger<HookPipeline> pipelineLogger)
{
    // Skip if no hooks are configured
    if (serverConfig.Hooks.PreInvoke is null or { Length: 0 } &&
        serverConfig.Hooks.PostInvoke is null or { Length: 0 })
    {
        return;
    }

    var pipeline = new HookPipeline(pipelineLogger);
    hookFactory.ConfigurePipeline(serverConfig.Hooks, pipeline);
    proxy.SetHookPipeline(pipeline);
}

void ConfigureHookPipelines(
    ProxyConfiguration configuration,
    McpProxyServer proxyServer,
    HookFactory hookFactory,
    ILogger<HookPipeline> pipelineLogger)
{
    foreach (var (serverName, serverConfig) in configuration.Mcp)
    {
        // Skip if no hooks are configured
        if (serverConfig.Hooks.PreInvoke is null or { Length: 0 } &&
            serverConfig.Hooks.PostInvoke is null or { Length: 0 })
        {
            continue;
        }

        var pipeline = new HookPipeline(pipelineLogger);
        hookFactory.ConfigurePipeline(serverConfig.Hooks, pipeline);
        proxyServer.AddHookPipeline(serverName, pipeline);
    }
}

void ConfigureLogging(IServiceCollection services, bool verbose)
{
    services.AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
    });
}

void RegisterCoreServices(IServiceCollection services, ProxyConfiguration configuration)
{
    services.AddSingleton(configuration);
    services.AddSingleton<ProxyClientHandlers>();
    services.AddSingleton<NotificationForwarder>();
    services.AddSingleton<McpClientManager>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<McpClientManager>>();
        var proxyClientHandlers = sp.GetRequiredService<ProxyClientHandlers>();
        var notificationForwarder = sp.GetRequiredService<NotificationForwarder>();
        return new McpClientManager(logger, proxyClientHandlers, notificationForwarder);
    });
    services.AddSingleton<HookFactory>();
    services.AddSingleton<McpProxyServer>();
    
    // Add telemetry services
    services.AddProxyTelemetry(configuration);
}

void ConfigureServerOptions(McpServerOptions options, ProxyConfiguration configuration)
{
    // Configure server info
    var serverInfo = configuration.Proxy.ServerInfo;
    options.ServerInfo = new Implementation
    {
        Name = serverInfo.Name,
        Version = serverInfo.Version
    };

    if (!string.IsNullOrEmpty(serverInfo.Instructions))
    {
        options.ServerInstructions = serverInfo.Instructions;
    }

    // Configure server experimental capabilities
    var serverCapabilities = configuration.Proxy.Capabilities.Server;
    if (serverCapabilities.Experimental is { Count: > 0 })
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Experimental = serverCapabilities.Experimental;
    }
}

/// <summary>
/// Transport type for the proxy server.
/// </summary>
enum TransportType
{
    /// <summary>
    /// Standard input/output transport.
    /// </summary>
    Stdio,

    /// <summary>
    /// HTTP/SSE transport.
    /// </summary>
    Http,

    /// <summary>
    /// Server-Sent Events transport (alias for Http).
    /// </summary>
    Sse
}

/// <summary>
/// Extension methods for configuring MCP proxy handlers.
/// </summary>
static class McpProxyBuilderExtensions
{
    /// <summary>
    /// Registers all MCP protocol handlers (Tools, Resources, Prompts, Subscriptions) that delegate to the proxy server.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpServerBuilder WithProxyHandlers(this IMcpServerBuilder builder)
    {
        return builder
            .WithListToolsHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ListToolsAsync(context, token);
            })
            .WithCallToolHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.CallToolAsync(context, token);
            })
            .WithListResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ListResourcesAsync(context, token);
            })
            .WithReadResourceHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ReadResourceAsync(context, token);
            })
            .WithListPromptsHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ListPromptsAsync(context, token);
            })
            .WithGetPromptHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.GetPromptAsync(context, token);
            })
            .WithSubscribeToResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.SubscribeToResourceAsync(context, token);
            })
            .WithUnsubscribeFromResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.UnsubscribeFromResourceAsync(context, token);
            });
    }

    /// <summary>
    /// Ensures the ProxyClientHandlers and NotificationForwarder have the McpServer reference set.
    /// This enables backend servers to forward sampling/elicitation/roots requests and notifications to the connected client.
    /// </summary>
    private static void EnsureProxyClientHandlersInitialized<TParams>(RequestContext<TParams> context) where TParams : class
    {
        var handlers = context.Server!.Services!.GetRequiredService<ProxyClientHandlers>();
        handlers.SetMcpServer(context.Server);

        var notificationForwarder = context.Server!.Services!.GetRequiredService<NotificationForwarder>();
        notificationForwarder.SetMcpServer(context.Server);
    }
}
