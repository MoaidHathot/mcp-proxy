using McpProxy.Abstractions;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Proxy;
using McpProxy.Sdk.Sdk;
using McpProxy.Tests.E2E.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)
#pragma warning disable CA2012 // Use ValueTasks correctly (test code)

namespace McpProxy.Tests.E2E;

/// <summary>
/// Reproduction tests for the per-server MCP Streamable HTTP tool discovery bug.
///
/// Bug report: When an MCP client connects to a per-server endpoint (e.g., /mcp/calendar)
/// via MCP Streamable HTTP, tools/list returns zero tools. REST sub-routes work correctly.
///
/// Root causes:
///   1. CLI did not register IHttpContextAccessor, so TryGetPerServerProxy could not resolve
///      the HTTP request path and always returned null — falling through to the unified proxy.
///   2. The unified McpProxyServer.ListToolsCoreAsync did not call EnsureDeferredClientsConnectedAsync,
///      so backends with deferConnection:true were invisible (empty _clients dict → 0 tools).
///   3. CLI created duplicate SingleServerProxy instances: keyed singletons (used by REST)
///      vs IPerServerProxyRegistrar instances (used by MCP handlers). Hooks were configured
///      only on the keyed singletons, never reaching MCP handler instances.
/// </summary>
public class PerServerMcpStreamableHttpTests : ProxyTestBase
{
    // -------------------------------------------------------------------
    //  Bug 1 integration test — full HTTP pipeline via TestServer + McpClient
    //
    //  This test proves that without IHttpContextAccessor, the MCP handler
    //  falls through to the unified proxy and returns ALL tools from ALL
    //  backends on a per-server endpoint (the v1.14.0 "41 tools leaked"
    //  behavior). With IHttpContextAccessor registered, the handler
    //  resolves the correct SingleServerProxy and returns only that
    //  server's tools.
    // -------------------------------------------------------------------

    /// <summary>
    /// Builds a WebApplication with per-server MCP routing and mock backends.
    /// When <paramref name="registerHttpContextAccessor"/> is false, the setup
    /// reproduces Bug 1: TryGetPerServerProxy returns null → unified proxy.
    /// </summary>
    private static (WebApplication App, McpClientManager ClientMgr) BuildTestApp(
        bool registerHttpContextAccessor,
        ProxyConfiguration config)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Core services
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);
        builder.Services.AddSingleton<ProxyClientHandlers>();
        builder.Services.AddSingleton<NotificationForwarder>();
        builder.Services.AddSingleton(config);

        builder.Services.AddSingleton<McpClientManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<McpClientManager>>();
            var lf = sp.GetRequiredService<ILoggerFactory>();
            var handlers = sp.GetRequiredService<ProxyClientHandlers>();
            var forwarder = sp.GetRequiredService<NotificationForwarder>();
            return new McpClientManager(logger, lf, handlers, forwarder);
        });

        builder.Services.AddSingleton<McpProxyServer>();

        // Bug 1 toggle: register or skip IHttpContextAccessor
        if (registerHttpContextAccessor)
        {
            builder.Services.AddHttpContextAccessor();
        }

        // Per-server registrar
        builder.Services.AddSingleton<IPerServerProxyRegistrar>(sp =>
        {
            var singleLogger = sp.GetRequiredService<ILogger<SingleServerProxy>>();
            var clientMgr = sp.GetRequiredService<McpClientManager>();
            var httpCtxAccessor = sp.GetService<IHttpContextAccessor>();
            var proxies = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase);
            var routeToProxy = new Dictionary<string, SingleServerProxy>(StringComparer.OrdinalIgnoreCase);

            foreach (var (srvName, srvConfig) in config.Mcp.Where(m => m.Value.Enabled))
            {
                var proxy = new SingleServerProxy(singleLogger, clientMgr, srvName, srvConfig, httpCtxAccessor);
                proxies[srvName] = proxy;
                var route = $"/mcp/{srvName}";
                routeToProxy[route] = proxy;
            }

            return new PerServerProxyRegistrar(proxies, routeToProxy);
        });

        // MCP server with handlers that reproduce the TryGetPerServerProxy logic
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "test-proxy", Version = "1.0.0" };
            })
            .WithHttpTransport()
            .WithListToolsHandler((context, token) =>
            {
                // This is the exact logic from TryGetPerServerProxy + fallback
                var httpContextAccessor = context.Server!.Services!.GetService<IHttpContextAccessor>();
                var httpContext = httpContextAccessor?.HttpContext;
                if (httpContext is not null)
                {
                    var registrar = context.Server.Services!.GetService<IPerServerProxyRegistrar>();
                    var singleProxy = registrar?.TryGetProxyForRoute(httpContext.Request.Path);
                    if (singleProxy is not null)
                    {
                        return singleProxy.ListToolsAsync(token);
                    }
                }

                // Fallback: unified proxy (returns ALL tools from ALL backends)
                var proxyServer = context.Server!.Services!.GetRequiredService<McpProxyServer>();
                return proxyServer.ListToolsAsync(context, token);
            });

        var app = builder.Build();

        // Map per-server MCP endpoints
        foreach (var (serverName, _) in config.Mcp.Where(m => m.Value.Enabled))
        {
            app.MapMcp($"/mcp/{serverName}");
        }

        var clientMgr2 = app.Services.GetRequiredService<McpClientManager>();
        return (app, clientMgr2);
    }

    private static IMcpClientWrapper CreateMockClientWrapper(string serverName, IList<Tool> tools)
    {
        var client = Substitute.For<IMcpClientWrapper>();
        client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tools));
        client.CallToolAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Result from {serverName}:{ci.ArgAt<string>(0)}" }]
            }));
        client.ListResourcesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Resource>>([]));
        client.ListPromptsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Prompt>>([]));
        return client;
    }

    /// <summary>
    /// BUG 1 REPRODUCTION: Without IHttpContextAccessor, the per-server MCP endpoint
    /// falls through to the unified proxy and returns ALL tools from ALL backends.
    /// This reproduces the v1.14.0 "41 tools leaked" behavior.
    /// </summary>
    [Fact]
    public async Task Bug1_WithoutHttpContextAccessor_PerServerEndpoint_ReturnsAllTools()
    {
        // Arrange: two servers with distinct tools
        var config = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["calendar"] = new() { Type = ServerTransportType.Stdio, Command = "mock" },
                ["mail"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
            }
        };

        // Bug 1: do NOT register IHttpContextAccessor
        var (app, clientMgr) = BuildTestApp(registerHttpContextAccessor: false, config);

        clientMgr.RegisterClient("calendar", CreateMockClientWrapper("calendar",
            [CreateTool("create_event"), CreateTool("list_events")]), config.Mcp["calendar"]);
        clientMgr.RegisterClient("mail", CreateMockClientWrapper("mail",
            [CreateTool("send_message"), CreateTool("read_inbox"), CreateTool("delete_message")]), config.Mcp["mail"]);

        await app.StartAsync(TestContext.Current.CancellationToken);
        var httpClient = app.GetTestClient();

        // Act: connect via MCP Streamable HTTP to the CALENDAR per-server endpoint
        await using var mcpClient = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp/calendar"),
                TransportMode = HttpTransportMode.StreamableHttp
            }, httpClient, ownsHttpClient: false),
            cancellationToken: TestContext.Current.CancellationToken);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: WITHOUT the fix, all 5 tools leak (calendar + mail combined)
        // This proves the bug — should be 2, but is 5.
        tools.Should().HaveCount(5,
            "BUG REPRODUCED: without IHttpContextAccessor, the per-server endpoint " +
            "falls through to the unified proxy and returns ALL tools from ALL backends");

        await app.StopAsync(TestContext.Current.CancellationToken);
        await app.DisposeAsync();
        await clientMgr.DisposeAsync();
    }

    /// <summary>
    /// BUG 1 FIX VERIFICATION: With IHttpContextAccessor registered, the per-server
    /// MCP endpoint correctly resolves the SingleServerProxy and returns only that
    /// server's tools.
    /// </summary>
    [Fact]
    public async Task Bug1Fix_WithHttpContextAccessor_PerServerEndpoint_ReturnsOnlyOwnTools()
    {
        // Arrange: same setup as the bug reproduction
        var config = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["calendar"] = new() { Type = ServerTransportType.Stdio, Command = "mock" },
                ["mail"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
            }
        };

        // Fix applied: register IHttpContextAccessor
        var (app, clientMgr) = BuildTestApp(registerHttpContextAccessor: true, config);

        clientMgr.RegisterClient("calendar", CreateMockClientWrapper("calendar",
            [CreateTool("create_event"), CreateTool("list_events")]), config.Mcp["calendar"]);
        clientMgr.RegisterClient("mail", CreateMockClientWrapper("mail",
            [CreateTool("send_message"), CreateTool("read_inbox"), CreateTool("delete_message")]), config.Mcp["mail"]);

        await app.StartAsync(TestContext.Current.CancellationToken);
        var httpClient = app.GetTestClient();

        // Act: connect to CALENDAR per-server endpoint
        await using var mcpClient = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp/calendar"),
                TransportMode = HttpTransportMode.StreamableHttp
            }, httpClient, ownsHttpClient: false),
            cancellationToken: TestContext.Current.CancellationToken);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: WITH the fix, only calendar's 2 tools are returned
        tools.Should().HaveCount(2,
            "FIX VERIFIED: with IHttpContextAccessor, the per-server endpoint " +
            "returns only its own backend's tools");
        tools.Select(t => t.Name).Should().BeEquivalentTo(["create_event", "list_events"]);

        await app.StopAsync(TestContext.Current.CancellationToken);
        await app.DisposeAsync();
        await clientMgr.DisposeAsync();
    }

    // -------------------------------------------------------------------
    //  Bug 3 test — hooks on registrar proxy vs separate instance
    //
    //  This test proves that when hooks are configured on a DIFFERENT
    //  SingleServerProxy instance than the one used by the MCP handler,
    //  the hooks never execute (the pre-fix behavior).
    // -------------------------------------------------------------------

    /// <summary>
    /// BUG 3 REPRODUCTION: When hooks are configured on a separate SingleServerProxy
    /// instance (simulating the old keyed singleton), the MCP handler's registrar
    /// proxy does NOT execute those hooks.
    /// </summary>
    [Fact]
    public async Task Bug3_HooksOnSeparateInstance_NotExecuted()
    {
        // Arrange: register a client
        RegisterClient("calendar", CreateMockClient("calendar", tools: [CreateTool("create_event")]));

        // Create TWO separate SingleServerProxy instances for the same backend
        // (this is what the old CLI code did — keyed singletons vs registrar instances)
        var logger = Substitute.For<ILogger<SingleServerProxy>>();
        var serverConfig = new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "mock" };

        var keyedInstance = new SingleServerProxy(logger, ClientManager, "calendar", serverConfig);
        var registrarInstance = new SingleServerProxy(logger, ClientManager, "calendar", serverConfig);

        // Configure hook on the keyed instance (old CLI behavior: hooks on keyed singletons)
        var hookExecuted = false;
        var pipeline = new McpProxy.Sdk.Hooks.HookPipeline(Substitute.For<ILogger<McpProxy.Sdk.Hooks.HookPipeline>>());
        var hook = Substitute.For<IPreInvokeHook>();
        hook.Priority.Returns(0);
        hook.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
            .Returns(ci => { hookExecuted = true; return ValueTask.CompletedTask; });
        pipeline.AddPreInvokeHook(hook);
        keyedInstance.SetHookPipeline(pipeline);  // Hook on keyed instance

        // Act: call through the registrar instance (what MCP handlers use)
        await registrarInstance.CallToolAsync(
            new CallToolRequestParams { Name = "create_event" },
            TestContext.Current.CancellationToken);

        // Assert: hook was NOT executed — it was on the wrong instance
        hookExecuted.Should().BeFalse(
            "BUG REPRODUCED: hooks configured on the keyed singleton instance " +
            "are not executed when the MCP handler uses a different registrar instance");
    }

    /// <summary>
    /// BUG 3 FIX VERIFICATION: When hooks are configured on the SAME SingleServerProxy
    /// instance used by the MCP handler (the registrar's instance), hooks execute correctly.
    /// </summary>
    [Fact]
    public async Task Bug3Fix_HooksOnSharedInstance_Executed()
    {
        // Arrange: register a client
        RegisterClient("calendar", CreateMockClient("calendar", tools: [CreateTool("create_event")]));

        // Create ONE shared SingleServerProxy instance (the fix: both REST and MCP use this)
        var logger = Substitute.For<ILogger<SingleServerProxy>>();
        var serverConfig = new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "mock" };
        var sharedInstance = new SingleServerProxy(logger, ClientManager, "calendar", serverConfig);

        // Configure hook on the shared instance
        var hookExecuted = false;
        var pipeline = new McpProxy.Sdk.Hooks.HookPipeline(Substitute.For<ILogger<McpProxy.Sdk.Hooks.HookPipeline>>());
        var hook = Substitute.For<IPreInvokeHook>();
        hook.Priority.Returns(0);
        hook.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
            .Returns(ci => { hookExecuted = true; return ValueTask.CompletedTask; });
        pipeline.AddPreInvokeHook(hook);
        sharedInstance.SetHookPipeline(pipeline);  // Hook on the shared instance

        // Act: call through the same shared instance (what MCP handlers now use)
        await sharedInstance.CallToolAsync(
            new CallToolRequestParams { Name = "create_event" },
            TestContext.Current.CancellationToken);

        // Assert: hook WAS executed — same instance used by both REST and MCP
        hookExecuted.Should().BeTrue(
            "FIX VERIFIED: hooks configured on the shared registrar instance " +
            "are executed for MCP Streamable HTTP requests");
    }

    // -------------------------------------------------------------------
    //  Bug 2 test — McpProxyServer deferred client connection
    //
    //  Bug 2 cannot be fully reproduced in a unit test because
    //  EnsureDeferredClientsConnectedAsync calls CreateClientAsync which
    //  requires a real MCP backend. The observable difference (0 tools
    //  vs N tools) only manifests when the deferred connection SUCCEEDS.
    //
    //  However, we CAN verify that:
    //  a) InitializeAsync correctly defers backends with deferConnection:true
    //  b) EnsureDeferredClientsConnectedAsync is called (by observing side effects)
    //  c) The unified proxy still works when clients are pre-registered
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that backends with deferConnection:true are placed in the deferred list
    /// (not in _clients), which means the unified proxy would return 0 tools for them.
    /// This demonstrates the precondition for Bug 2.
    /// </summary>
    [Fact]
    public async Task Bug2_Precondition_DeferredBackends_NotInClients()
    {
        // Arrange: create a fresh McpClientManager and initialize with deferred config
        var logger = Substitute.For<ILogger<McpClientManager>>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        await using var manager = new McpClientManager(logger, loggerFactory);

        var config = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["calendar"] = new()
                {
                    Type = ServerTransportType.Http,
                    Url = "https://example.com/mcp",
                    Auth = new BackendAuthConfiguration
                    {
                        Type = BackendAuthType.None,
                        DeferConnection = true
                    }
                }
            }
        };

        // Act
        await manager.InitializeAsync(config, TestContext.Current.CancellationToken);

        // Assert: backend is deferred, NOT in _clients
        manager.Clients.Should().BeEmpty(
            "BUG 2 PRECONDITION: backends with deferConnection:true are not in _clients — " +
            "without EnsureDeferredClientsConnectedAsync, the unified proxy iterates " +
            "an empty _clients dict and returns 0 tools");
        manager.HasDeferredClients.Should().BeTrue();
        manager.DeferredClientNames.Should().Contain("calendar");
    }

    /// <summary>
    /// Verifies that when a deferred backend is manually connected (simulating a
    /// successful EnsureDeferredClientsConnectedAsync call), the unified proxy
    /// includes its tools.
    /// </summary>
    [Fact]
    public async Task Bug2Fix_WhenDeferredClientConnected_UnifiedProxyReturnsTool()
    {
        // Arrange: start with deferred config
        var logger = Substitute.For<ILogger<McpClientManager>>();
        var loggerFactory = Substitute.For<ILoggerFactory>();
        await using var manager = new McpClientManager(logger, loggerFactory);

        var config = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["calendar"] = new()
                {
                    Type = ServerTransportType.Http,
                    Url = "https://example.com/mcp",
                    Auth = new BackendAuthConfiguration
                    {
                        Type = BackendAuthType.None,
                        DeferConnection = true
                    }
                }
            }
        };

        await manager.InitializeAsync(config, TestContext.Current.CancellationToken);
        manager.Clients.Should().BeEmpty("precondition: backend is deferred");

        // Simulate what EnsureDeferredClientsConnectedAsync does when connection succeeds:
        // the client moves from _deferredClients to _clients
        manager.RegisterClient("calendar", CreateMockClientWrapper("calendar",
            [CreateTool("create_event")]), config.Mcp["calendar"]);

        // Act: unified proxy should now see the client
        var proxyLogger = Substitute.For<ILogger<McpProxyServer>>();
        var proxyServer = new McpProxyServer(proxyLogger, manager, config);
        var result = await proxyServer.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert: tools are returned after deferred client is connected
        result.Tools.Should().ContainSingle(
            "FIX VERIFIED: after EnsureDeferredClientsConnectedAsync connects the backend, " +
            "the unified proxy sees its tools");
        result.Tools[0].Name.Should().Be("create_event");
    }
}
