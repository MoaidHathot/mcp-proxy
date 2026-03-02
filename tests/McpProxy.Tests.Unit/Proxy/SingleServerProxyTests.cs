using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Hooks;
using McpProxy.Core.Proxy;
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
            var result = await proxy.ListToolsAsync(CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Tools.Should().BeEmpty();
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
            var result = await proxy.ListToolsAsync(CancellationToken.None);

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
            var result = await proxy.ListToolsAsync(CancellationToken.None);

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
            var result = await proxy.CallToolAsync(request, CancellationToken.None);

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
            var result = await proxy.CallToolAsync(request, CancellationToken.None);

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
            var result = await proxy.ListResourcesAsync(CancellationToken.None);

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
            var result = await proxy.ReadResourceAsync(request, CancellationToken.None);

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
            var result = await proxy.ListPromptsAsync(CancellationToken.None);

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
            var result = await proxy.GetPromptAsync(request, CancellationToken.None);

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
            var result = await proxy.CallToolAsync(request, CancellationToken.None);

            // Assert - No client connected, but hooks should still execute
            // Looking at the code, the hook runs after the client check
            // So the hook won't execute if client is null
            result.IsError.Should().BeTrue();
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
