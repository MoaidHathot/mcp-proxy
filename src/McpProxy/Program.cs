using System.CommandLine;
using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.Sdk.Authentication;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Debugging;
using McpProxy.Sdk.Hooks;
using McpProxy.Sdk.Logging;
using McpProxy.Sdk.Proxy;
using McpProxy.Sdk.Sdk;
using McpProxy.Sdk.Telemetry;
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

var serverOption = new Option<string[]>("--server", "-s")
{
    Description = "Select specific server(s) from the configuration (others are disabled). Can be specified multiple times.",
    AllowMultipleArgumentsPerToken = true
};

var rootCommand = new RootCommand("MCP Proxy - Aggregates multiple MCP servers into a single endpoint");
rootCommand.Options.Add(transportOption);
rootCommand.Options.Add(configOption);
rootCommand.Options.Add(portOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(serverOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var transport = parseResult.GetValue(transportOption);
    var configPath = parseResult.GetValue(configOption);
    var port = parseResult.GetValue(portOption);
    var verbose = parseResult.GetValue(verboseOption);
    var selectedServers = parseResult.GetValue(serverOption);
    await RunProxyAsync(transport, configPath, port, verbose, selectedServers, cancellationToken).ConfigureAwait(false);
});

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

async Task RunProxyAsync(TransportType transport, string? configPath, int port, bool verbose, string[]? selectedServers, CancellationToken cancellationToken)
{
    // Resolve config path
    configPath ??= Environment.GetEnvironmentVariable("MCP_PROXY_CONFIG_PATH");

    // Expand environment variables in the config path.
    // Supports ${VAR}, $VAR, %VAR% (Windows), and ~ (home directory).
    // This ensures the path works even when the MCP client does not expand variables.
    if (!string.IsNullOrEmpty(configPath))
    {
        // Expand ~ to home directory
        if (configPath.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configPath = home + configPath[1..];
        }

        // Expand %VAR% (Windows-style) via .NET built-in
        configPath = Environment.ExpandEnvironmentVariables(configPath);

        // Expand ${VAR} and $VAR (Unix-style)
        configPath = Regex.Replace(configPath, @"\$\{(\w+)\}|\$(\w+)", match =>
        {
            var varName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return Environment.GetEnvironmentVariable(varName) ?? match.Value;
        });
    }

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

    // Filter servers if --server was specified
    if (selectedServers is { Length: > 0 })
    {
        var selectedSet = new HashSet<string>(selectedServers, StringComparer.OrdinalIgnoreCase);

        // Validate that all selected servers exist in the configuration
        var unknownServers = selectedSet.Except(configuration.Mcp.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknownServers.Count > 0)
        {
            Console.Error.WriteLine($"Error: Unknown server(s): {string.Join(", ", unknownServers)}");
            Console.Error.WriteLine($"Available servers: {string.Join(", ", configuration.Mcp.Keys)}");
            Environment.Exit(1);
            return;
        }

        // Disable all servers not in the selected set
        foreach (var (name, config) in configuration.Mcp)
        {
            if (!selectedSet.Contains(name))
            {
                config.Enabled = false;
            }
        }
    }

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

    // Required for TryGetPerServerProxy to resolve per-server routes via HttpContext.Request.Path
    builder.Services.AddHttpContextAccessor();

    // Register SingleServerProxy instances for PerServer mode.
    // A single IPerServerProxyRegistrar holds all instances, shared by both MCP Streamable HTTP
    // handlers and REST sub-routes, ensuring hooks and configuration are applied consistently.
    if (configuration.Proxy.Routing.Mode == RoutingMode.PerServer)
    {
        builder.Services.AddSingleton<IPerServerProxyRegistrar>(sp =>
        {
            var singleLogger = sp.GetRequiredService<ILogger<SingleServerProxy>>();
            var clientMgr = sp.GetRequiredService<McpClientManager>();
            var httpCtxAccessor = sp.GetService<IHttpContextAccessor>();
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase);
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase);

            foreach (var (srvName, srvConfig) in configuration.Mcp.Where(m => m.Value.Enabled))
            {
                var proxy = new SingleServerProxy(singleLogger, clientMgr, srvName, srvConfig, httpCtxAccessor);
                proxies[srvName] = proxy;
                var route = srvConfig.Route ?? $"{configuration.Proxy.Routing.BasePath}/{srvName}";
                routeToProxy[route] = proxy;
            }

            return new PerServerProxyRegistrar(proxies, routeToProxy);
        });
    }
    else
    {
        builder.Services.AddSingleton<IPerServerProxyRegistrar>(new NoOpPerServerProxyRegistrar());
    }

    // Configure MCP Server with HTTP transport and all handlers
    builder.Services
        .AddMcpServer(options => ConfigureServerOptions(options, configuration))
        .WithHttpTransport(transport =>
        {
            // Enable legacy SSE endpoints (/sse and /message) for backward compatibility
            // with MCP clients that use the older SSE transport protocol.
#pragma warning disable MCP9004 // EnableLegacySse is obsolete but required for backward compatibility
            transport.EnableLegacySse = true;
#pragma warning restore MCP9004
        })
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
        var registrar = app.Services.GetRequiredService<IPerServerProxyRegistrar>();
        foreach (var (serverName, serverConfig) in configuration.Mcp.Where(m => m.Value.Enabled))
        {
            var singleServerProxy = registrar.GetProxy(serverName);
            ConfigureSingleServerHookPipeline(serverName, serverConfig, singleServerProxy, hookFactory, pipelineLogger);
        }
    }
    
    ProxyLogger.ProxyStarted(logger, url);

    // Map MCP endpoints based on routing mode
    if (configuration.Proxy.Routing.Mode == RoutingMode.PerServer)
    {
        var registrar = app.Services.GetRequiredService<IPerServerProxyRegistrar>();

        // PerServer mode: Each server gets its own endpoint
        // Map to base path for unified endpoint (still available for discovery)
        app.MapMcp(configuration.Proxy.Routing.BasePath);
        
        // Map individual server endpoints
        foreach (var (serverName, serverConfig) in configuration.Mcp.Where(m => m.Value.Enabled))
        {
            var route = serverConfig.Route ?? $"{configuration.Proxy.Routing.BasePath}/{serverName}";
            
            // Map MCP Streamable HTTP endpoint for this server (MCP protocol clients)
            app.MapMcp(route);
            
            // Map REST sub-routes for backward compatibility
            MapSingleServerEndpoint(app, serverName, route, registrar);
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

    // Map debug health endpoint (localhost-only, HTTP mode only)
    if (configuration.Proxy.Debug.HealthEndpoint)
    {
        MapDebugHealthEndpoint(app, logger, configuration.Proxy.Debug.HealthEndpointPath);
    }

    await app.RunAsync().ConfigureAwait(false);
}

void MapSingleServerEndpoint(WebApplication app, string serverName, string route, IPerServerProxyRegistrar registrar)
{
    // Create MCP-like endpoints for single server
    // These endpoints allow direct access to a specific backend server
    app.MapPost($"{route}/tools/list", async (HttpContext context) =>
    {
        var proxy = registrar.GetProxy(serverName);
        var result = await proxy.ListToolsAsync(context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
    
    app.MapPost($"{route}/tools/call", async (HttpContext context, ModelContextProtocol.Protocol.CallToolRequestParams request) =>
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
    
    app.MapPost($"{route}/resources/read", async (HttpContext context, ModelContextProtocol.Protocol.ReadResourceRequestParams request) =>
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
    
    app.MapPost($"{route}/prompts/get", async (HttpContext context, ModelContextProtocol.Protocol.GetPromptRequestParams request) =>
    {
        var proxy = registrar.GetProxy(serverName);
        var result = await proxy.GetPromptAsync(request, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result);
    });
}

void MapDebugHealthEndpoint(WebApplication app, ILogger logger, string healthPath)
{
    app.MapGet(healthPath, async (HttpContext context) =>
    {
        // Localhost-only check
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null || !System.Net.IPAddress.IsLoopback(remoteIp))
        {
            context.Response.StatusCode = 403;
            return Results.Json(new { error = "Forbidden", message = "Debug health endpoint is only accessible from localhost" });
        }

        var healthTracker = app.Services.GetRequiredService<IHealthTracker>();
        var status = await healthTracker.GetHealthStatusAsync(context.RequestAborted).ConfigureAwait(false);
        
        // Set appropriate status code based on health
        context.Response.StatusCode = status.Status switch
        {
            HealthStatus.Healthy => 200,
            HealthStatus.Degraded => 200,
            HealthStatus.Unhealthy => 503,
            _ => 200
        };
        
        return Results.Json(status);
    });
    
    ProxyLogger.DebugHealthEndpointEnabled(logger, healthPath);
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
    
    // Register debugging services
    RegisterDebuggingServices(services, configuration);
    
    services.AddSingleton<McpClientManager>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<McpClientManager>>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var proxyClientHandlers = sp.GetRequiredService<ProxyClientHandlers>();
        var notificationForwarder = sp.GetRequiredService<NotificationForwarder>();
        var healthTracker = sp.GetService<IHealthTracker>();
        var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
        return new McpClientManager(logger, loggerFactory, proxyClientHandlers, notificationForwarder, healthTracker, httpContextAccessor);
    });
    services.AddSingleton<HookFactory>(sp =>
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var memoryCache = sp.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var metrics = sp.GetService<ProxyMetrics>();
        var requestDumper = sp.GetService<IRequestDumper>();
        return new HookFactory(loggerFactory, memoryCache, metrics, requestDumper);
    });
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

void RegisterDebuggingServices(IServiceCollection services, ProxyConfiguration configuration)
{
    var debugConfig = configuration.Proxy.Debug;
    
    // Register hook tracer
    if (debugConfig.HookTracing)
    {
        services.AddSingleton<IHookTracer, HookTracer>();
    }
    else
    {
        services.AddSingleton<IHookTracer>(NullHookTracer.Instance);
    }
    
    // Register request dumper
    if (debugConfig.Dump.Enabled)
    {
        services.AddSingleton<IRequestDumper>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RequestDumper>>();
            return new RequestDumper(logger, debugConfig.Dump);
        });
    }
    else
    {
        services.AddSingleton<IRequestDumper>(NullRequestDumper.Instance);
    }
    
    // Register health tracker
    if (debugConfig.HealthEndpoint)
    {
        services.AddSingleton<IHealthTracker, HealthTracker>();
    }
    else
    {
        services.AddSingleton<IHealthTracker>(NullHealthTracker.Instance);
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
                if (TryGetPerServerProxy(context) is { } singleProxy)
                    return singleProxy.ListToolsAsync(token);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ListToolsAsync(context, token);
            })
            .WithCallToolHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                if (TryGetPerServerProxy(context) is { } singleProxy)
                    return singleProxy.CallToolAsync(context.Params!, token);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.CallToolAsync(context, token);
            })
            .WithListResourcesHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                if (TryGetPerServerProxy(context) is { } singleProxy)
                    return singleProxy.ListResourcesAsync(token);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ListResourcesAsync(context, token);
            })
            .WithReadResourceHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                if (TryGetPerServerProxy(context) is { } singleProxy)
                    return singleProxy.ReadResourceAsync(context.Params!, token);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ReadResourceAsync(context, token);
            })
            .WithListPromptsHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                if (TryGetPerServerProxy(context) is { } singleProxy)
                    return singleProxy.ListPromptsAsync(token);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxy.ListPromptsAsync(context, token);
            })
            .WithGetPromptHandler((context, token) =>
            {
                EnsureProxyClientHandlersInitialized(context);
                if (TryGetPerServerProxy(context) is { } singleProxy)
                    return singleProxy.GetPromptAsync(context.Params!, token);
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
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
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
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
                var proxy = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return await proxy.UnsubscribeFromResourceAsync(context, token).ConfigureAwait(false);
            });
    }

    private static SingleServerProxy? TryGetPerServerProxy<TParams>(RequestContext<TParams> context) where TParams : class
    {
        var httpContextAccessor = context.Server!.Services!.GetService<IHttpContextAccessor>();
        var httpContext = httpContextAccessor?.HttpContext;
        if (httpContext is null) return null;

        var registrar = context.Server.Services!.GetService<IPerServerProxyRegistrar>();
        return registrar?.TryGetProxyForRoute(httpContext.Request.Path);
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
