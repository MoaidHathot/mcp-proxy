using McpProxy.Core.Configuration;
using McpProxy.Core.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Proxy;

public class ResourceSubscriptionTests : IAsyncDisposable
{
    private readonly ILogger<McpProxyServer> _proxyLogger;
    private readonly ILogger<McpClientManager> _clientManagerLogger;
    private readonly ILogger<ResourceSubscriptionManager> _subscriptionManagerLogger;
    private readonly McpClientManager _clientManager;
    private readonly ProxyConfiguration _configuration;
    private readonly McpProxyServer _proxyServer;

    public ResourceSubscriptionTests()
    {
        _proxyLogger = Substitute.For<ILogger<McpProxyServer>>();
        _clientManagerLogger = Substitute.For<ILogger<McpClientManager>>();
        _subscriptionManagerLogger = Substitute.For<ILogger<ResourceSubscriptionManager>>();

        _configuration = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["server1"] = new ServerConfiguration
                {
                    Type = ServerTransportType.Stdio,
                    Command = "test"
                },
                ["server2"] = new ServerConfiguration
                {
                    Type = ServerTransportType.Stdio,
                    Command = "test"
                }
            }
        };

        _clientManager = new McpClientManager(_clientManagerLogger);
        _proxyServer = new McpProxyServer(_proxyLogger, _clientManager, _configuration);
    }

    public async ValueTask DisposeAsync()
    {
        await _clientManager.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void RegisterMockClient(string serverName, IMcpClientWrapper client, ServerConfiguration? config = null)
    {
        config ??= new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "test" };
        _clientManager.RegisterClient(serverName, client, config);
    }

    public class SubscribeToResourceCoreAsyncTests : ResourceSubscriptionTests
    {
        [Fact]
        public async Task SubscribeToResourceCoreAsync_WhenResourceExists_ForwardsToBackend()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _proxyServer.SubscribeToResourceCoreAsync("file:///test.txt", CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            await mockClient.Received(1).SubscribeToResourceAsync("file:///test.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubscribeToResourceCoreAsync_WhenResourceNotFound_ReturnsEmptyResult()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Resource>());

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _proxyServer.SubscribeToResourceCoreAsync("file:///nonexistent.txt", CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            await mockClient.DidNotReceive().SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubscribeToResourceCoreAsync_WithMultipleServers_FindsCorrectBackend()
        {
            // Arrange
            var mockClient1 = Substitute.For<IMcpClientWrapper>();
            mockClient1.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Resource>());

            var mockClient2 = Substitute.For<IMcpClientWrapper>();
            mockClient2.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///server2.txt", Name = "server2" }]);
            mockClient2.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient1);
            RegisterMockClient("server2", mockClient2);

            // Act
            var result = await _proxyServer.SubscribeToResourceCoreAsync("file:///server2.txt", CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            await mockClient1.DidNotReceive().SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await mockClient2.Received(1).SubscribeToResourceAsync("file:///server2.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubscribeToResourceCoreAsync_WhenBackendThrows_PropagatesException()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("Backend error")));

            RegisterMockClient("server1", mockClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _proxyServer.SubscribeToResourceCoreAsync("file:///test.txt", CancellationToken.None));
            exception.Message.Should().Be("Backend error");
        }
    }

    public class UnsubscribeFromResourceCoreAsyncTests : ResourceSubscriptionTests
    {
        [Fact]
        public async Task UnsubscribeFromResourceCoreAsync_WhenResourceExists_ForwardsToBackend()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _proxyServer.UnsubscribeFromResourceCoreAsync("file:///test.txt", CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            await mockClient.Received(1).UnsubscribeFromResourceAsync("file:///test.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UnsubscribeFromResourceCoreAsync_WhenResourceNotFound_ReturnsEmptyResult()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Resource>());

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _proxyServer.UnsubscribeFromResourceCoreAsync("file:///nonexistent.txt", CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            await mockClient.DidNotReceive().UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UnsubscribeFromResourceCoreAsync_WithMultipleServers_FindsCorrectBackend()
        {
            // Arrange
            var mockClient1 = Substitute.For<IMcpClientWrapper>();
            mockClient1.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Resource>());

            var mockClient2 = Substitute.For<IMcpClientWrapper>();
            mockClient2.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///server2.txt", Name = "server2" }]);
            mockClient2.UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient1);
            RegisterMockClient("server2", mockClient2);

            // Act
            var result = await _proxyServer.UnsubscribeFromResourceCoreAsync("file:///server2.txt", CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            await mockClient1.DidNotReceive().UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await mockClient2.Received(1).UnsubscribeFromResourceAsync("file:///server2.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UnsubscribeFromResourceCoreAsync_WhenBackendThrows_PropagatesException()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("Backend error")));

            RegisterMockClient("server1", mockClient);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _proxyServer.UnsubscribeFromResourceCoreAsync("file:///test.txt", CancellationToken.None));
            exception.Message.Should().Be("Backend error");
        }
    }
}

public class ResourceSubscriptionManagerTests : IAsyncDisposable
{
    private readonly ILogger<ResourceSubscriptionManager> _logger;
    private readonly ILogger<McpClientManager> _clientManagerLogger;
    private readonly McpClientManager _clientManager;
    private readonly ResourceSubscriptionManager _subscriptionManager;

    public ResourceSubscriptionManagerTests()
    {
        _logger = Substitute.For<ILogger<ResourceSubscriptionManager>>();
        _clientManagerLogger = Substitute.For<ILogger<McpClientManager>>();
        _clientManager = new McpClientManager(_clientManagerLogger);
        _subscriptionManager = new ResourceSubscriptionManager(_logger, _clientManager);
    }

    public async ValueTask DisposeAsync()
    {
        await _clientManager.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void RegisterMockClient(string serverName, IMcpClientWrapper client, ServerConfiguration? config = null)
    {
        config ??= new ServerConfiguration { Type = ServerTransportType.Stdio, Command = "test" };
        _clientManager.RegisterClient(serverName, client, config);
    }

    public class SubscribeAsyncTests : ResourceSubscriptionManagerTests
    {
        [Fact]
        public async Task SubscribeAsync_WhenResourceExists_ReturnsTrue()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _subscriptionManager.SubscribeAsync("file:///test.txt");

            // Assert
            result.Should().BeTrue();
            await mockClient.Received(1).SubscribeToResourceAsync("file:///test.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubscribeAsync_WhenResourceNotFound_ReturnsFalse()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Resource>());

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _subscriptionManager.SubscribeAsync("file:///nonexistent.txt");

            // Assert
            result.Should().BeFalse();
            await mockClient.DidNotReceive().SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubscribeAsync_TracksSubscription()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Act
            await _subscriptionManager.SubscribeAsync("file:///test.txt");

            // Assert
            _subscriptionManager.GetSubscriptionServer("file:///test.txt").Should().Be("server1");
        }

        [Fact]
        public async Task SubscribeAsync_WhenBackendThrows_ReturnsFalse()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("Backend error")));

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _subscriptionManager.SubscribeAsync("file:///test.txt");

            // Assert
            result.Should().BeFalse();
        }
    }

    public class UnsubscribeAsyncTests : ResourceSubscriptionManagerTests
    {
        [Fact]
        public async Task UnsubscribeAsync_WhenSubscribed_ReturnsTrue()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            mockClient.UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // First subscribe
            await _subscriptionManager.SubscribeAsync("file:///test.txt");

            // Act
            var result = await _subscriptionManager.UnsubscribeAsync("file:///test.txt");

            // Assert
            result.Should().BeTrue();
            await mockClient.Received(1).UnsubscribeFromResourceAsync("file:///test.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UnsubscribeAsync_WhenNotSubscribedButResourceExists_ReturnsTrue()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Act - unsubscribe without prior subscribe
            var result = await _subscriptionManager.UnsubscribeAsync("file:///test.txt");

            // Assert
            result.Should().BeTrue();
            await mockClient.Received(1).UnsubscribeFromResourceAsync("file:///test.txt", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UnsubscribeAsync_WhenResourceNotFound_ReturnsFalse()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Resource>());

            RegisterMockClient("server1", mockClient);

            // Act
            var result = await _subscriptionManager.UnsubscribeAsync("file:///nonexistent.txt");

            // Assert
            result.Should().BeFalse();
            await mockClient.DidNotReceive().UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UnsubscribeAsync_RemovesTracking()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([new Resource { Uri = "file:///test.txt", Name = "test" }]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            mockClient.UnsubscribeFromResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Subscribe first
            await _subscriptionManager.SubscribeAsync("file:///test.txt");
            _subscriptionManager.GetSubscriptionServer("file:///test.txt").Should().Be("server1");

            // Act
            await _subscriptionManager.UnsubscribeAsync("file:///test.txt");

            // Assert - subscription should be removed from tracking
            _subscriptionManager.GetSubscriptionServer("file:///test.txt").Should().BeNull();
        }
    }

    public class GetActiveSubscriptionsTests : ResourceSubscriptionManagerTests
    {
        [Fact]
        public async Task GetActiveSubscriptions_ReturnsAllSubscriptions()
        {
            // Arrange
            var mockClient = Substitute.For<IMcpClientWrapper>();
            mockClient.ListResourcesAsync(Arg.Any<CancellationToken>())
                .Returns([
                    new Resource { Uri = "file:///test1.txt", Name = "test1" },
                    new Resource { Uri = "file:///test2.txt", Name = "test2" }
                ]);
            mockClient.SubscribeToResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            RegisterMockClient("server1", mockClient);

            // Subscribe to multiple resources
            await _subscriptionManager.SubscribeAsync("file:///test1.txt");
            await _subscriptionManager.SubscribeAsync("file:///test2.txt");

            // Act
            var subscriptions = _subscriptionManager.GetActiveSubscriptions();

            // Assert
            subscriptions.Should().HaveCount(2);
            subscriptions.Should().ContainKey("file:///test1.txt");
            subscriptions.Should().ContainKey("file:///test2.txt");
        }
    }
}
