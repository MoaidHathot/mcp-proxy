using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Debugging;
using McpProxy.Sdk.Proxy;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)
#pragma warning disable CA2012 // Use ValueTasks correctly (test code)
#pragma warning disable CA2213 // Disposable fields should be disposed (mocked fields don't need disposal)

namespace McpProxy.Tests.Unit.Proxy;

public class McpClientManagerTests : IAsyncDisposable
{
    private readonly ILogger<McpClientManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHealthTracker _healthTracker;

    public McpClientManagerTests()
    {
        _logger = Substitute.For<ILogger<McpClientManager>>();
        _loggerFactory = Substitute.For<ILoggerFactory>();
        _healthTracker = Substitute.For<IHealthTracker>();
    }

    private McpClientManager CreateManager(IHealthTracker? healthTracker = null) =>
        new(_logger, _loggerFactory, healthTracker: healthTracker ?? _healthTracker);

    private static IMcpClientWrapper CreateMockClient()
    {
        var client = Substitute.For<IMcpClientWrapper>();
        client.DisposeAsync().Returns(ValueTask.CompletedTask);
        return client;
    }

    private static ServerConfiguration CreateStdioConfig() => new()
    {
        Type = ServerTransportType.Stdio,
        Command = "mock"
    };

    public class RegisterClientTests : McpClientManagerTests
    {
        [Fact]
        public void Registers_Client_Successfully()
        {
            // Arrange
            var manager = CreateManager();
            var client = CreateMockClient();

            // Act
            manager.RegisterClient("server1", client, CreateStdioConfig());

            // Assert
            manager.Clients.Should().ContainKey("server1");
            manager.Clients["server1"].Client.Should().BeSameAs(client);
            manager.Clients["server1"].Name.Should().Be("server1");
        }

        [Fact]
        public void Overwrites_Existing_Client()
        {
            // Arrange
            var manager = CreateManager();
            var client1 = CreateMockClient();
            var client2 = CreateMockClient();

            // Act
            manager.RegisterClient("server1", client1, CreateStdioConfig());
            manager.RegisterClient("server1", client2, CreateStdioConfig());

            // Assert
            manager.Clients["server1"].Client.Should().BeSameAs(client2);
        }
    }

    public class GetClientTests : McpClientManagerTests
    {
        [Fact]
        public void Returns_Client_When_Registered()
        {
            // Arrange
            var manager = CreateManager();
            var client = CreateMockClient();
            manager.RegisterClient("server1", client, CreateStdioConfig());

            // Act
            var result = manager.GetClient("server1");

            // Assert
            result.Should().NotBeNull();
            result!.Client.Should().BeSameAs(client);
        }

        [Fact]
        public void Returns_Null_When_Not_Registered()
        {
            // Arrange
            var manager = CreateManager();

            // Act
            var result = manager.GetClient("nonexistent");

            // Assert
            result.Should().BeNull();
        }
    }

    public class ClientsPropertyTests : McpClientManagerTests
    {
        [Fact]
        public void Returns_Empty_When_No_Clients()
        {
            // Arrange
            var manager = CreateManager();

            // Act & Assert
            manager.Clients.Should().BeEmpty();
        }

        [Fact]
        public void Returns_All_Registered_Clients()
        {
            // Arrange
            var manager = CreateManager();
            manager.RegisterClient("s1", CreateMockClient(), CreateStdioConfig());
            manager.RegisterClient("s2", CreateMockClient(), CreateStdioConfig());

            // Act & Assert
            manager.Clients.Should().HaveCount(2);
            manager.Clients.Should().ContainKey("s1");
            manager.Clients.Should().ContainKey("s2");
        }
    }

    public class DeferredClientPropertyTests : McpClientManagerTests
    {
        [Fact]
        public void HasDeferredClients_Returns_False_When_Empty()
        {
            // Arrange
            var manager = CreateManager();

            // Act & Assert
            manager.HasDeferredClients.Should().BeFalse();
        }

        [Fact]
        public void DeferredClientNames_Returns_Empty_When_No_Deferred()
        {
            // Arrange
            var manager = CreateManager();

            // Act & Assert
            manager.DeferredClientNames.Should().BeEmpty();
        }
    }

    public class HealthTrackerTests : McpClientManagerTests
    {
        [Fact]
        public void Returns_NullHealthTracker_When_Not_Provided()
        {
            // Arrange
            var manager = new McpClientManager(_logger, _loggerFactory);

            // Act & Assert
            manager.HealthTracker.Should().BeSameAs(NullHealthTracker.Instance);
        }

        [Fact]
        public void Returns_Provided_HealthTracker()
        {
            // Arrange
            var tracker = Substitute.For<IHealthTracker>();
            var manager = CreateManager(tracker);

            // Act & Assert
            manager.HealthTracker.Should().BeSameAs(tracker);
        }
    }

    public class DisposeAsyncTests : McpClientManagerTests
    {
        [Fact]
        public async Task Disposes_All_Clients()
        {
            // Arrange
            await using var manager = CreateManager();
            var client1 = CreateMockClient();
            var client2 = CreateMockClient();
            manager.RegisterClient("s1", client1, CreateStdioConfig());
            manager.RegisterClient("s2", client2, CreateStdioConfig());

            // Act
            await manager.DisposeAsync();

            // Assert
            await client1.Received(1).DisposeAsync();
            await client2.Received(1).DisposeAsync();
        }

        [Fact]
        public async Task Records_Disconnection_In_HealthTracker()
        {
            // Arrange
            var tracker = Substitute.For<IHealthTracker>();
            await using var manager = CreateManager(tracker);
            manager.RegisterClient("s1", CreateMockClient(), CreateStdioConfig());

            // Act
            await manager.DisposeAsync();

            // Assert
            tracker.Received(1).RecordConnectionState("s1", connected: false);
        }

        [Fact]
        public async Task Is_Idempotent()
        {
            // Arrange
            await using var manager = CreateManager();
            var client = CreateMockClient();
            manager.RegisterClient("s1", client, CreateStdioConfig());

            // Act
            await manager.DisposeAsync();
            await manager.DisposeAsync();

            // Assert - should only dispose once
            await client.Received(1).DisposeAsync();
        }

        [Fact]
        public async Task Continues_When_Client_Disposal_Throws()
        {
            // Arrange
            await using var manager = CreateManager();
            var failingClient = Substitute.For<IMcpClientWrapper>();
            failingClient.DisposeAsync()
                .Returns(ValueTask.FromException(new InvalidOperationException("Disposal error")));

            var goodClient = CreateMockClient();
            manager.RegisterClient("failing", failingClient, CreateStdioConfig());
            manager.RegisterClient("good", goodClient, CreateStdioConfig());

            // Act - should not throw
            await manager.DisposeAsync();

            // Assert - both should be attempted
            await failingClient.Received(1).DisposeAsync();
            await goodClient.Received(1).DisposeAsync();
        }

        [Fact]
        public async Task Clears_Clients_After_Disposal()
        {
            // Arrange
            var manager = CreateManager();
            manager.RegisterClient("s1", CreateMockClient(), CreateStdioConfig());

            // Act
            await manager.DisposeAsync();

            // Assert
            manager.Clients.Should().BeEmpty();
        }
    }

    public class InitializeAsyncTests : McpClientManagerTests
    {
        [Fact]
        public async Task Defers_ForwardAuthorization_Backends()
        {
            // Arrange
            await using var manager = CreateManager();
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["forward-auth-server"] = new()
                    {
                        Type = ServerTransportType.Sse,
                        Url = "https://example.com/mcp/sse",
                        Auth = new BackendAuthConfiguration
                        {
                            Type = BackendAuthType.ForwardAuthorization
                        }
                    }
                }
            };

            // Act
            await manager.InitializeAsync(config, TestContext.Current.CancellationToken);

            // Assert
            manager.Clients.Should().BeEmpty();
            manager.HasDeferredClients.Should().BeTrue();
            manager.DeferredClientNames.Should().Contain("forward-auth-server");
        }

        [Fact]
        public async Task Defers_Backends_With_DeferConnection()
        {
            // Arrange
            await using var manager = CreateManager();
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["deferred-server"] = new()
                    {
                        Type = ServerTransportType.Sse,
                        Url = "https://example.com/mcp/sse",
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

            // Assert
            manager.Clients.Should().BeEmpty();
            manager.HasDeferredClients.Should().BeTrue();
        }

        [Fact]
        public async Task Skips_Disabled_Servers()
        {
            // Arrange
            await using var manager = CreateManager();
            var config = new ProxyConfiguration
            {
                Mcp = new Dictionary<string, ServerConfiguration>
                {
                    ["disabled-server"] = new()
                    {
                        Type = ServerTransportType.Stdio,
                        Command = "echo",
                        Enabled = false
                    }
                }
            };

            // Act
            await manager.InitializeAsync(config, TestContext.Current.CancellationToken);

            // Assert
            manager.Clients.Should().BeEmpty();
            manager.HasDeferredClients.Should().BeFalse();
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
    }
}
