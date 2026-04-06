using System.Text.Json;
using System.Text.Json.Nodes;
using McpProxy.Sdk.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Proxy;

public class NotificationForwarderTests
{
    private readonly ILogger<NotificationForwarder> _logger;
    private readonly NotificationForwarder _forwarder;

    public NotificationForwarderTests()
    {
        _logger = Substitute.For<ILogger<NotificationForwarder>>();
        _forwarder = new NotificationForwarder(_logger);
    }

    public class ForwardNotificationAsyncTests : NotificationForwarderTests
    {
        [Fact]
        public async Task ForwardNotificationAsync_WhenNoServerSet_DoesNotThrow()
        {
            // Arrange
            var method = "notifications/test";
            var parameters = JsonSerializer.Deserialize<JsonElement>("{}");

            // Act
            var act = () => _forwarder.ForwardNotificationAsync(method, parameters);

            // Assert
            await act.Should().NotThrowAsync();
        }
    }

    public class ForwardProgressNotificationAsyncTests : NotificationForwarderTests
    {
        [Fact]
        public async Task ForwardProgressNotificationAsync_WhenNoServerSet_DoesNotThrow()
        {
            // Arrange
            var progressToken = "test-token";
            var progress = 50.0;

            // Act
            var act = () => _forwarder.ForwardProgressNotificationAsync(progressToken, progress);

            // Assert
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task ForwardProgressNotificationAsync_WithAllParameters_DoesNotThrow()
        {
            // Arrange
            var progressToken = "test-token";
            var progress = 75.0;
            var total = 100.0;
            var message = "Processing...";

            // Act
            var act = () => _forwarder.ForwardProgressNotificationAsync(progressToken, progress, total, message);

            // Assert
            await act.Should().NotThrowAsync();
        }
    }

    public class CreateProgressNotificationHandlerTests : NotificationForwarderTests
    {
        [Fact]
        public async Task CreateProgressNotificationHandler_WithNullParams_ReturnsWithoutError()
        {
            // Arrange
            var handler = _forwarder.CreateProgressNotificationHandler("test-server");
            var notification = new JsonRpcNotification
            {
                Method = NotificationMethods.ProgressNotification,
                Params = null
            };

            // Act & Assert - should not throw
            await handler(notification, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CreateProgressNotificationHandler_WithValidParams_ParsesAndForwards()
        {
            // Arrange
            var handler = _forwarder.CreateProgressNotificationHandler("test-server");
            var progressValue = new ProgressNotificationValue
            {
                Progress = 50,
                Total = 100,
                Message = "Half done"
            };
            var progressParams = new ProgressNotificationParams
            {
                ProgressToken = new ProgressToken("test-token-123"),
                Progress = progressValue
            };

            var paramsJson = JsonSerializer.SerializeToNode(progressParams);
            var notification = new JsonRpcNotification
            {
                Method = NotificationMethods.ProgressNotification,
                Params = paramsJson
            };

            // Act & Assert - should not throw even without McpServer set
            await handler(notification, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CreateProgressNotificationHandler_WithInvalidJson_DoesNotThrow()
        {
            // Arrange
            var handler = _forwarder.CreateProgressNotificationHandler("test-server");
            var notification = new JsonRpcNotification
            {
                Method = NotificationMethods.ProgressNotification,
                Params = JsonNode.Parse("{\"invalid\": true}")
            };

            // Act & Assert - should catch JsonException internally
            await handler(notification, TestContext.Current.CancellationToken);
        }
    }

    public class CreateNotificationHandlerTests : NotificationForwarderTests
    {
        [Fact]
        public async Task CreateNotificationHandler_WithNullParams_ReturnsWithoutError()
        {
            // Arrange
            var handler = _forwarder.CreateNotificationHandler("test-server", "notifications/tools/list_changed");
            var notification = new JsonRpcNotification
            {
                Method = "notifications/tools/list_changed",
                Params = null
            };

            // Act & Assert - should not throw
            await handler(notification, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CreateNotificationHandler_WithParams_ConvertsThenForwards()
        {
            // Arrange
            var handler = _forwarder.CreateNotificationHandler("test-server", "notifications/resources/updated");
            var notification = new JsonRpcNotification
            {
                Method = "notifications/resources/updated",
                Params = JsonNode.Parse("{\"uri\": \"file:///test.txt\"}")
            };

            // Act & Assert - should not throw even without McpServer set
            await handler(notification, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CreateNotificationHandler_DifferentMethods_CreatesSeparateHandlers()
        {
            // Arrange
            var toolsHandler = _forwarder.CreateNotificationHandler("server1", "notifications/tools/list_changed");
            var resourcesHandler = _forwarder.CreateNotificationHandler("server1", "notifications/resources/list_changed");
            var promptsHandler = _forwarder.CreateNotificationHandler("server1", "notifications/prompts/list_changed");

            var toolsNotification = new JsonRpcNotification
            {
                Method = "notifications/tools/list_changed",
                Params = null
            };

            var resourcesNotification = new JsonRpcNotification
            {
                Method = "notifications/resources/list_changed",
                Params = null
            };

            var promptsNotification = new JsonRpcNotification
            {
                Method = "notifications/prompts/list_changed",
                Params = null
            };

            // Act & Assert - all should work independently
            var ct = TestContext.Current.CancellationToken;
            await toolsHandler(toolsNotification, ct);
            await resourcesHandler(resourcesNotification, ct);
            await promptsHandler(promptsNotification, ct);
        }
    }

    public class HandlerCreationTests : NotificationForwarderTests
    {
        [Fact]
        public void CreateProgressNotificationHandler_ReturnsNonNullHandler()
        {
            // Act
            var handler = _forwarder.CreateProgressNotificationHandler("test-server");

            // Assert
            handler.Should().NotBeNull();
        }

        [Fact]
        public void CreateNotificationHandler_ReturnsNonNullHandler()
        {
            // Act
            var handler = _forwarder.CreateNotificationHandler("test-server", "notifications/test");

            // Assert
            handler.Should().NotBeNull();
        }

        [Fact]
        public void CreateNotificationHandler_DifferentServers_CreatesSeparateHandlers()
        {
            // Act
            var handler1 = _forwarder.CreateNotificationHandler("server1", "notifications/tools/list_changed");
            var handler2 = _forwarder.CreateNotificationHandler("server2", "notifications/tools/list_changed");

            // Assert
            handler1.Should().NotBeNull();
            handler2.Should().NotBeNull();
            handler1.Should().NotBeSameAs(handler2);
        }
    }
}
