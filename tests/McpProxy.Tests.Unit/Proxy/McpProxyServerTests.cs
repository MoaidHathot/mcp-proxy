using McpProxy.Abstractions;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Hooks;
using McpProxy.Sdk.Hooks.BuiltIn;
using McpProxy.Sdk.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Text.Json;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)
#pragma warning disable CA2012 // Use ValueTasks correctly (test code)
#pragma warning disable CA2213 // Disposable fields should be disposed (mocked fields don't need disposal)

namespace McpProxy.Tests.Unit.Proxy;

public class McpProxyServerTests : IAsyncDisposable
{
    private readonly ILogger<McpProxyServer> _proxyLogger;
    private readonly ILogger<McpClientManager> _clientManagerLogger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HookPipeline> _hookPipelineLogger;
    private readonly McpClientManager _clientManager;

    public McpProxyServerTests()
    {
        _proxyLogger = Substitute.For<ILogger<McpProxyServer>>();
        _clientManagerLogger = Substitute.For<ILogger<McpClientManager>>();
        _loggerFactory = Substitute.For<ILoggerFactory>();
        _hookPipelineLogger = Substitute.For<ILogger<HookPipeline>>();
        _clientManager = new McpClientManager(_clientManagerLogger, _loggerFactory);
    }

    private IMcpClientWrapper CreateMockClient(
        string serverName,
        IList<Tool>? tools = null)
    {
        var client = Substitute.For<IMcpClientWrapper>();

        client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Tool>>(tools ?? []));

        client.CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var toolName = callInfo.ArgAt<string>(0);
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Result from {serverName}:{toolName}" }]
                });
            });

        client.ListResourcesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Resource>>([]));

        client.ListPromptsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Prompt>>([]));

        return client;
    }

    private void RegisterClient(string serverName, IMcpClientWrapper client, ServerConfiguration? config = null)
    {
        var serverConfig = config ?? new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock"
        };
        _clientManager.RegisterClient(serverName, client, serverConfig);
    }

    private McpProxyServer CreateProxyServer(ProxyConfiguration? config = null, IToolCache? toolCache = null)
    {
        var proxyConfig = config ?? new ProxyConfiguration();
        return new McpProxyServer(_proxyLogger, _clientManager, proxyConfig, toolCache);
    }

    private static Tool CreateTool(string name)
    {
        return new Tool
        {
            Name = name,
            Description = $"Description for {name}",
            InputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement
        };
    }

    public class CallToolCoreAsyncTests : McpProxyServerTests
    {
        [Fact]
        public async Task Returns_Error_When_Tool_Not_Found()
        {
            // Arrange
            var server = CreateProxyServer();
            var request = new CallToolRequestParams { Name = "nonexistent_tool" };

            // Act
            var result = await server.CallToolCoreAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.IsError.Should().BeTrue();
            result.Content.Should().ContainSingle();
            var text = result.Content[0] as TextContentBlock;
            text!.Text.Should().Contain("nonexistent_tool");
            text.Text.Should().Contain("not found");
        }

        [Fact]
        public async Task Calls_Backend_With_Original_Tool_Name()
        {
            // Arrange
            var client = CreateMockClient("server1", [CreateTool("my_tool")]);
            RegisterClient("server1", client);
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["server1"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
                }
            };
            var server = CreateProxyServer(config);
            var request = new CallToolRequestParams
            {
                Name = "my_tool",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["arg1"] = JsonDocument.Parse("\"value1\"").RootElement
                }
            };

            // Act
            var result = await server.CallToolCoreAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.IsError.Should().NotBe(true);
            await client.Received(1).CallToolAsync(
                "my_tool",
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Executes_PreInvoke_Hook_Before_Tool_Call()
        {
            // Arrange
            var hookExecuted = false;
            var client = CreateMockClient("server1", [CreateTool("my_tool")]);
            RegisterClient("server1", client);
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["server1"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
                }
            };
            var server = CreateProxyServer(config);

            var hook = Substitute.For<IPreInvokeHook>();
            hook.Priority.Returns(0);
            hook.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
                .Returns(callInfo =>
                {
                    hookExecuted = true;
                    return ValueTask.CompletedTask;
                });

            var pipeline = new HookPipeline(_hookPipelineLogger);
            pipeline.AddPreInvokeHook(hook);
            server.AddHookPipeline("server1", pipeline);

            var request = new CallToolRequestParams { Name = "my_tool" };

            // Act
            await server.CallToolCoreAsync(request, TestContext.Current.CancellationToken);

            // Assert
            hookExecuted.Should().BeTrue();
        }

        [Fact]
        public async Task Disposes_Timeout_CTS_After_Tool_Call()
        {
            // Arrange
            var client = CreateMockClient("server1", [CreateTool("my_tool")]);
            RegisterClient("server1", client);
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["server1"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
                }
            };
            var server = CreateProxyServer(config);

            // Create a hook that adds a timeout CTS to the context
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var hook = Substitute.For<IPreInvokeHook>();
            hook.Priority.Returns(0);
            hook.OnPreInvokeAsync(Arg.Any<HookContext<CallToolRequestParams>>())
                .Returns(callInfo =>
                {
                    var ctx = callInfo.ArgAt<HookContext<CallToolRequestParams>>(0);
                    ctx.Items[TimeoutHook.TimeoutCtsKey] = cts;
                    ctx.CancellationToken = cts.Token;
                    return ValueTask.CompletedTask;
                });

            var pipeline = new HookPipeline(_hookPipelineLogger);
            pipeline.AddPreInvokeHook(hook);
            server.AddHookPipeline("server1", pipeline);

            var request = new CallToolRequestParams { Name = "my_tool" };

            // Act
            await server.CallToolCoreAsync(request, TestContext.Current.CancellationToken);

            // Assert - the CTS should have been disposed (accessing Token throws ObjectDisposedException)
            var act = () => cts.Token;
            act.Should().Throw<ObjectDisposedException>();
        }
    }

    public class ListToolsCoreAsyncTests : McpProxyServerTests
    {
        [Fact]
        public async Task Returns_Empty_When_No_Clients()
        {
            // Arrange
            var server = CreateProxyServer();

            // Act
            var result = await server.ListToolsCoreAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Tools.Should().BeEmpty();
        }

        [Fact]
        public async Task Aggregates_Tools_From_Multiple_Servers()
        {
            // Arrange
            var client1 = CreateMockClient("server1", [CreateTool("tool_a")]);
            var client2 = CreateMockClient("server2", [CreateTool("tool_b"), CreateTool("tool_c")]);
            RegisterClient("server1", client1);
            RegisterClient("server2", client2);
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["server1"] = new() { Type = ServerTransportType.Stdio, Command = "mock" },
                    ["server2"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
                }
            };
            var server = CreateProxyServer(config);

            // Act
            var result = await server.ListToolsCoreAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Tools.Should().HaveCount(3);
            result.Tools.Select(t => t.Name).Should().Contain(["tool_a", "tool_b", "tool_c"]);
        }

        [Fact]
        public async Task Continues_When_One_Server_Throws()
        {
            // Arrange
            var client1 = Substitute.For<IMcpClientWrapper>();
            client1.ListToolsAsync(Arg.Any<CancellationToken>())
                .Returns<IList<Tool>>(_ => throw new InvalidOperationException("Backend error"));

            var client2 = CreateMockClient("server2", [CreateTool("tool_b")]);
            RegisterClient("server1", client1);
            RegisterClient("server2", client2);
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["server1"] = new() { Type = ServerTransportType.Stdio, Command = "mock" },
                    ["server2"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
                }
            };
            var server = CreateProxyServer(config);

            // Act
            var result = await server.ListToolsCoreAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Tools.Should().ContainSingle();
            result.Tools[0].Name.Should().Be("tool_b");
        }
    }

    public class FindToolAsyncTests : McpProxyServerTests
    {
        [Fact]
        public async Task FindsTool_Via_CallToolCoreAsync_On_Second_Server_After_First_Throws()
        {
            // Arrange
            var failingClient = Substitute.For<IMcpClientWrapper>();
            failingClient.ListToolsAsync(Arg.Any<CancellationToken>())
                .Returns<IList<Tool>>(_ => throw new InvalidOperationException("Backend down"));

            var workingClient = CreateMockClient("server2", [CreateTool("shared_tool")]);

            RegisterClient("server1", failingClient);
            RegisterClient("server2", workingClient);
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["server1"] = new() { Type = ServerTransportType.Stdio, Command = "mock" },
                    ["server2"] = new() { Type = ServerTransportType.Stdio, Command = "mock" }
                }
            };
            var server = CreateProxyServer(config);

            var request = new CallToolRequestParams { Name = "shared_tool" };

            // Act
            var result = await server.CallToolCoreAsync(request, TestContext.Current.CancellationToken);

            // Assert - should find the tool on server2 and return a result
            result.IsError.Should().NotBe(true);
        }
    }

    public class GetAuthenticationResultTests : McpProxyServerTests
    {
        [Fact]
        public async Task Tool_Not_Found_Returns_Error_Without_Auth_Context()
        {
            // Arrange
            // No HttpContextAccessor, so GetAuthenticationResult() returns null
            var server = CreateProxyServer();
            var request = new CallToolRequestParams { Name = "nonexistent" };

            // Act
            var result = await server.CallToolCoreAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.IsError.Should().BeTrue();
        }
    }

    public class InvalidateToolCacheTests : McpProxyServerTests
    {
        [Fact]
        public void InvalidateToolCache_Does_Not_Throw_For_Unknown_Server()
        {
            // Arrange
            var server = CreateProxyServer();

            // Act & Assert - should not throw
            var act = () => server.InvalidateToolCache("unknown_server");
            act.Should().NotThrow();
        }

        [Fact]
        public void InvalidateAllToolCaches_Does_Not_Throw()
        {
            // Arrange
            var server = CreateProxyServer();

            // Act & Assert
            var act = () => server.InvalidateAllToolCaches();
            act.Should().NotThrow();
        }
    }

    public class HookPipelineTests : McpProxyServerTests
    {
        [Fact]
        public void AddHookPipeline_And_GetHookPipeline_RoundTrip()
        {
            // Arrange
            var server = CreateProxyServer();
            var pipeline = new HookPipeline(_hookPipelineLogger);

            // Act
            server.AddHookPipeline("server1", pipeline);
            var retrieved = server.GetHookPipeline("server1");

            // Assert
            retrieved.Should().BeSameAs(pipeline);
        }

        [Fact]
        public void GetHookPipeline_Returns_Null_For_Unknown_Server()
        {
            // Arrange
            var server = CreateProxyServer();

            // Act
            var result = server.GetHookPipeline("unknown");

            // Assert
            result.Should().BeNull();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _clientManager.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
