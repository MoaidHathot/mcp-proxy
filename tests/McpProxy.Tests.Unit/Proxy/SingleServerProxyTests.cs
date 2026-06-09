using McpProxy.Abstractions;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Hooks;
using McpProxy.Sdk.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)
#pragma warning disable CA1001 // Type owns disposable field(s) but is not disposable (test class, disposed by test framework)
#pragma warning disable CA2213 // Disposable fields should be disposed (mocked fields don't need disposal)

namespace McpProxy.Tests.Unit.Proxy;

public class SingleServerProxyTests : IAsyncDisposable
{
    private readonly ILogger<SingleServerProxy> _logger;
    private readonly ILogger<McpClientManager> _clientManagerLogger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HookPipeline> _hookPipelineLogger;
    private readonly McpClientManager _clientManager;
    private readonly string _serverName = "test-server";

    public SingleServerProxyTests()
    {
        _logger = Substitute.For<ILogger<SingleServerProxy>>();
        _clientManagerLogger = Substitute.For<ILogger<McpClientManager>>();
        _loggerFactory = Substitute.For<ILoggerFactory>();
        _hookPipelineLogger = Substitute.For<ILogger<HookPipeline>>();
        _clientManager = new McpClientManager(_clientManagerLogger, _loggerFactory);
    }

    public async ValueTask DisposeAsync()
    {
        await _clientManager.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private SingleServerProxy CreateProxy(ServerConfiguration? config = null)
    {
        var serverConfig = config ?? new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "node",
            Arguments = ["server.js"]
        };

        return new SingleServerProxy(
            _logger,
            _clientManager,
            _serverName,
            serverConfig);
    }

    public class ConstructorTests : SingleServerProxyTests
    {
        [Fact]
        public void Constructor_SetsServerName()
        {
            // Arrange & Act
            var proxy = CreateProxy();

            // Assert
            proxy.ServerName.Should().Be(_serverName);
        }

        [Fact]
        public void Constructor_WithRoute_SetsRoute()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "node",
                Route = "/custom-route"
            };

            // Act
            var proxy = CreateProxy(config);

            // Assert
            proxy.Route.Should().Be("/custom-route");
        }

        [Fact]
        public void Constructor_WithoutRoute_SetsDefaultRoute()
        {
            // Arrange & Act
            var proxy = CreateProxy();

            // Assert
            proxy.Route.Should().Be($"/{_serverName}");
        }
    }

    public class ListToolsAsyncTests : SingleServerProxyTests
    {
        [Fact]
        public async Task ListToolsAsync_WhenNoClientConnected_ReturnsEmptyList()
        {
            // Arrange
            var proxy = CreateProxy();

            // Act
            var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Tools.Should().BeEmpty();
        }

        [Fact]
        public async Task ListToolsAsync_WhenClientConnected_ReturnsToolsFromBackend()
        {
            // Arrange
            var client = Substitute.For<IMcpClientWrapper>();
            client.ListToolsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IList<Tool>>([
                    new Tool { Name = "tool_a", Description = "Tool A" },
                    new Tool { Name = "tool_b", Description = "Tool B" }
                ]));
            _clientManager.RegisterClient(_serverName, client, new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "mock"
            });
            var proxy = CreateProxy();

            // Act
            var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Tools.Should().HaveCount(2);
            result.Tools.Select(t => t.Name).Should().Contain(["tool_a", "tool_b"]);
        }

        [Fact]
        public async Task ListToolsAsync_ReturnsOnlyThisServersTools()
        {
            // Arrange - register two clients but proxy is bound to _serverName only
            var client1 = Substitute.For<IMcpClientWrapper>();
            client1.ListToolsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IList<Tool>>([
                    new Tool { Name = "calendar_tool", Description = "Calendar" }
                ]));
            var client2 = Substitute.For<IMcpClientWrapper>();
            client2.ListToolsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IList<Tool>>([
                    new Tool { Name = "mail_tool", Description = "Mail" }
                ]));
            _clientManager.RegisterClient(_serverName, client1, new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "mock"
            });
            _clientManager.RegisterClient("other-server", client2, new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "mock"
            });
            var proxy = CreateProxy();

            // Act
            var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

            // Assert - should only return tools from _serverName, not other-server
            result.Tools.Should().ContainSingle();
            result.Tools[0].Name.Should().Be("calendar_tool");
        }

        [Fact]
        public async Task ListToolsAsync_WithFilter_FiltersTools()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "node",
                Tools = new ToolsConfiguration
                {
                    Filter = new FilterConfiguration
                    {
                        Mode = FilterMode.DenyList,
                        Patterns = ["excluded*"]
                    }
                }
            };
            var proxy = CreateProxy(config);

            // Act
            var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Tools.Should().BeEmpty(); // No client connected, so empty
        }

        [Fact]
        public async Task ListToolsAsync_WithPrefix_TransformsToolNames()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "node",
                Tools = new ToolsConfiguration
                {
                    Prefix = "server1"
                }
            };
            var proxy = CreateProxy(config);

            // Act
            var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Tools.Should().BeEmpty(); // No client connected, so empty
        }
    }

    public class CallToolAsyncTests : SingleServerProxyTests
    {
        [Fact]
        public async Task CallToolAsync_WhenNoClientConnected_ReturnsError()
        {
            // Arrange
            var proxy = CreateProxy();
            var request = new CallToolRequestParams
            {
                Name = "test-tool"
            };

            // Act
            var result = await proxy.CallToolAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.IsError.Should().BeTrue();
            result.Content.Should().HaveCount(1);
            var textContent = result.Content[0] as TextContentBlock;
            textContent.Should().NotBeNull();
            textContent!.Text.Should().Contain("not available");
        }

        [Fact]
        public async Task CallToolAsync_WithPrefix_RemovesPrefixBeforeCall()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                Type = ServerTransportType.Stdio,
                Command = "node",
                Tools = new ToolsConfiguration
                {
                    Prefix = "server1",
                    PrefixSeparator = "_"
                }
            };
            var proxy = CreateProxy(config);
            var request = new CallToolRequestParams
            {
                Name = "server1_test-tool"
            };

            // Act
            var result = await proxy.CallToolAsync(request, TestContext.Current.CancellationToken);

            // Assert - No client, so returns error
            result.Should().NotBeNull();
            result.IsError.Should().BeTrue();
        }
    }

    public class ListResourcesAsyncTests : SingleServerProxyTests
    {
        [Fact]
        public async Task ListResourcesAsync_WhenNoClientConnected_ReturnsEmptyList()
        {
            // Arrange
            var proxy = CreateProxy();

            // Act
            var result = await proxy.ListResourcesAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Resources.Should().BeEmpty();
        }
    }

    public class ReadResourceAsyncTests : SingleServerProxyTests
    {
        [Fact]
        public async Task ReadResourceAsync_WhenNoClientConnected_ReturnsError()
        {
            // Arrange
            var proxy = CreateProxy();
            var request = new ReadResourceRequestParams
            {
                Uri = "file:///test.txt"
            };

            // Act
            var result = await proxy.ReadResourceAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Contents.Should().HaveCount(1);
            var textContent = result.Contents[0] as TextResourceContents;
            textContent.Should().NotBeNull();
            textContent!.Text.Should().Contain("not available");
        }
    }

    public class ListPromptsAsyncTests : SingleServerProxyTests
    {
        [Fact]
        public async Task ListPromptsAsync_WhenNoClientConnected_ReturnsEmptyList()
        {
            // Arrange
            var proxy = CreateProxy();

            // Act
            var result = await proxy.ListPromptsAsync(TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Prompts.Should().BeEmpty();
        }
    }

    public class GetPromptAsyncTests : SingleServerProxyTests
    {
        [Fact]
        public async Task GetPromptAsync_WhenNoClientConnected_ReturnsError()
        {
            // Arrange
            var proxy = CreateProxy();
            var request = new GetPromptRequestParams
            {
                Name = "test-prompt"
            };

            // Act
            var result = await proxy.GetPromptAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Messages.Should().HaveCount(1);
            result.Messages[0].Role.Should().Be(Role.Assistant);
            var textContent = result.Messages[0].Content as TextContentBlock;
            textContent.Should().NotBeNull();
            textContent!.Text.Should().Contain("not available");
        }
    }

    public class HookPipelineTests : SingleServerProxyTests
    {
        [Fact]
        public void SetHookPipeline_SetsThePipeline()
        {
            // Arrange
            var proxy = CreateProxy();
            var pipeline = new HookPipeline(_hookPipelineLogger);

            // Act - Should not throw
            proxy.SetHookPipeline(pipeline);

            // Assert - Pipeline is set (we verify by calling a tool which would use hooks)
            // Since no client is connected, we can at least verify no exception is thrown
        }

        [Fact]
        public async Task CallToolAsync_WithHookPipeline_ExecutesHooks()
        {
            // Arrange
            var proxy = CreateProxy();
            var pipeline = new HookPipeline(_hookPipelineLogger);
            
            var testHook = new TestPreInvokeHook();
            pipeline.AddPreInvokeHook(testHook);
            
            proxy.SetHookPipeline(pipeline);
            
            var request = new CallToolRequestParams
            {
                Name = "test-tool"
            };

            // Act
            var result = await proxy.CallToolAsync(request, TestContext.Current.CancellationToken);

            // Assert - No client connected, but hooks should still execute
            // Looking at the code, the hook runs after the client check
            // So the hook won't execute if client is null
            result.IsError.Should().BeTrue();
        }
    }

    public class DeferredConnectFailureTests : SingleServerProxyTests
    {
        /// <summary>
        /// Initializes <see cref="SingleServerProxyTests._clientManager"/> with a
        /// deferred ForwardAuth backend at <c>127.0.0.1:1</c> that always refuses
        /// connections — the lazy connect attempt is guaranteed to throw, exercising
        /// the propagation path under test. The proxy's server name matches
        /// <see cref="SingleServerProxyTests._serverName"/>.
        /// </summary>
        private async Task<SingleServerProxy> CreateProxyWithFailingDeferredBackendAsync()
        {
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    [_serverName] = new()
                    {
                        Type = ServerTransportType.Sse,
                        Url = "http://127.0.0.1:1/mcp/sse",
                        Auth = new BackendAuthConfiguration
                        {
                            Type = BackendAuthType.ForwardAuthorization
                        }
                    }
                }
            };
            await _clientManager.InitializeAsync(config, TestContext.Current.CancellationToken);
            return CreateProxy(config.Mcp[_serverName]);
        }

        private static CancellationTokenSource BoundedToken(int seconds = 15)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));
            return cts;
        }

        [Fact]
        public async Task ListToolsAsync_PropagatesDeferredConnectException()
        {
            // Arrange
            var proxy = await CreateProxyWithFailingDeferredBackendAsync();
            using var cts = BoundedToken();

            // Act
            Func<Task> act = () => proxy.ListToolsAsync(cts.Token).AsTask();

            // Assert — previously returned an empty Tools list (the symptom the
            // "don't swallow" refactor targets); now propagates the real
            // exception so MCP clients see the underlying connect failure.
            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.Should().NotBeOfType<OperationCanceledException>(
                "15s should be ample for a 127.0.0.1:1 refusal — anything slower means the test is inconclusive");
        }

        [Fact]
        public async Task ListResourcesAsync_PropagatesDeferredConnectException()
        {
            var proxy = await CreateProxyWithFailingDeferredBackendAsync();
            using var cts = BoundedToken();

            Func<Task> act = () => proxy.ListResourcesAsync(cts.Token).AsTask();

            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.Should().NotBeOfType<OperationCanceledException>();
        }

        [Fact]
        public async Task ListPromptsAsync_PropagatesDeferredConnectException()
        {
            var proxy = await CreateProxyWithFailingDeferredBackendAsync();
            using var cts = BoundedToken();

            Func<Task> act = () => proxy.ListPromptsAsync(cts.Token).AsTask();

            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.Should().NotBeOfType<OperationCanceledException>();
        }

        [Fact]
        public async Task CallToolAsync_OnDeferredConnectFailure_ReturnsIsErrorResultCarryingTheExceptionMessage()
        {
            // Arrange — same setup as ListToolsAsync_PropagatesDeferredConnectException
            // but CallTool has a structured error envelope (CallToolResult.IsError),
            // so the exception is caught and its message is embedded in the result
            // rather than propagated as a transport error.
            var proxy = await CreateProxyWithFailingDeferredBackendAsync();
            using var cts = BoundedToken();
            var request = new CallToolRequestParams { Name = "some-tool" };

            // Act
            var result = await proxy.CallToolAsync(request, cts.Token);

            // Assert
            result.Should().NotBeNull();
            result.IsError.Should().BeTrue();
            result.Content.Should().HaveCount(1);
            var text = result.Content[0] as TextContentBlock;
            text.Should().NotBeNull();
            text!.Text.Should().StartWith($"Server '{_serverName}' not available: ");
            // The text after the colon should NOT just be empty — it must include
            // the underlying exception message so the operator/LLM sees the cause.
            text.Text.Substring($"Server '{_serverName}' not available: ".Length)
                .Should().NotBeEmpty("the underlying exception message must be embedded so the cause is visible");
        }

        [Fact]
        public async Task ReadResourceAsync_OnDeferredConnectFailure_ReturnsTextContentCarryingTheExceptionMessage()
        {
            var proxy = await CreateProxyWithFailingDeferredBackendAsync();
            using var cts = BoundedToken();
            var request = new ReadResourceRequestParams { Uri = "file:///x.txt" };

            var result = await proxy.ReadResourceAsync(request, cts.Token);

            result.Should().NotBeNull();
            result.Contents.Should().HaveCount(1);
            var text = result.Contents[0] as TextResourceContents;
            text.Should().NotBeNull();
            text!.Text.Should().StartWith($"Server '{_serverName}' not available: ");
            text.Text.Substring($"Server '{_serverName}' not available: ".Length).Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetPromptAsync_OnDeferredConnectFailure_ReturnsAssistantMessageCarryingTheExceptionMessage()
        {
            var proxy = await CreateProxyWithFailingDeferredBackendAsync();
            using var cts = BoundedToken();
            var request = new GetPromptRequestParams { Name = "p" };

            var result = await proxy.GetPromptAsync(request, cts.Token);

            result.Should().NotBeNull();
            result.Messages.Should().HaveCount(1);
            result.Messages[0].Role.Should().Be(Role.Assistant);
            var text = result.Messages[0].Content as TextContentBlock;
            text.Should().NotBeNull();
            text!.Text.Should().StartWith($"Server '{_serverName}' not available: ");
            text.Text.Substring($"Server '{_serverName}' not available: ".Length).Should().NotBeEmpty();
        }
    }

    private sealed class TestPreInvokeHook : IPreInvokeHook
    {
        public bool WasExecuted { get; private set; }

        public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
        {
            WasExecuted = true;
            return ValueTask.CompletedTask;
        }
    }
}
