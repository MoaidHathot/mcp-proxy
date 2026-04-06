using McpProxy.SDK.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Proxy;

public class ProxyClientHandlersTests
{
    private readonly ILogger<ProxyClientHandlers> _logger;
    private readonly ProxyClientHandlers _handlers;

    public ProxyClientHandlersTests()
    {
        _logger = Substitute.For<ILogger<ProxyClientHandlers>>();
        _handlers = new ProxyClientHandlers(_logger);
    }

    public class CapabilityPropertiesTests : ProxyClientHandlersTests
    {
        [Fact]
        public void HasSamplingSupport_WhenNoServerSet_ReturnsFalse()
        {
            // Act & Assert
            _handlers.HasSamplingSupport.Should().BeFalse();
        }

        [Fact]
        public void HasElicitationSupport_WhenNoServerSet_ReturnsFalse()
        {
            // Act & Assert
            _handlers.HasElicitationSupport.Should().BeFalse();
        }

        [Fact]
        public void HasRootsSupport_WhenNoServerSet_ReturnsFalse()
        {
            // Act & Assert
            _handlers.HasRootsSupport.Should().BeFalse();
        }
    }

    public class HandleSamplingAsyncTests : ProxyClientHandlersTests
    {
        [Fact]
        public async Task HandleSamplingAsync_WhenNoServerSet_ReturnsErrorResult()
        {
            // Arrange
            var request = new CreateMessageRequestParams
            {
                Messages = [],
                MaxTokens = 100
            };
            var progress = Substitute.For<IProgress<ProgressNotificationValue>>();

            // Act
            var result = await _handlers.HandleSamplingAsync(request, progress, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Model.Should().Be("error");
            result.StopReason.Should().Be("error");
            result.Content.Should().HaveCount(1);
            var textContent = result.Content[0] as TextContentBlock;
            textContent.Should().NotBeNull();
            textContent!.Text.Should().Contain("not initialized");
        }

        [Fact]
        public async Task HandleSamplingAsync_WhenRequestIsNull_ReturnsErrorResult()
        {
            // Arrange
            var progress = Substitute.For<IProgress<ProgressNotificationValue>>();

            // Act
            var result = await _handlers.HandleSamplingAsync(null, progress, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Model.Should().Be("error");
            result.Content.Should().HaveCount(1);
            var textContent = result.Content[0] as TextContentBlock;
            textContent.Should().NotBeNull();
            textContent!.Text.Should().Contain("not initialized");
        }
    }

    public class HandleElicitationAsyncTests : ProxyClientHandlersTests
    {
        [Fact]
        public async Task HandleElicitationAsync_WhenNoServerSet_ReturnsDeclinedResult()
        {
            // Arrange
            var request = new ElicitRequestParams
            {
                Message = "Test prompt"
            };

            // Act
            var result = await _handlers.HandleElicitationAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Action.Should().Be("decline");
        }

        [Fact]
        public async Task HandleElicitationAsync_WhenRequestIsNull_ReturnsDeclinedResult()
        {
            // Act
            var result = await _handlers.HandleElicitationAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Action.Should().Be("decline");
        }
    }

    public class HandleRootsAsyncTests : ProxyClientHandlersTests
    {
        [Fact]
        public async Task HandleRootsAsync_WhenNoServerSet_ReturnsEmptyRoots()
        {
            // Arrange
            var request = new ListRootsRequestParams();

            // Act
            var result = await _handlers.HandleRootsAsync(request, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Roots.Should().BeEmpty();
        }

        [Fact]
        public async Task HandleRootsAsync_WhenRequestIsNull_ReturnsEmptyRoots()
        {
            // Act
            var result = await _handlers.HandleRootsAsync(null, TestContext.Current.CancellationToken);

            // Assert
            result.Should().NotBeNull();
            result.Roots.Should().BeEmpty();
        }
    }
}
