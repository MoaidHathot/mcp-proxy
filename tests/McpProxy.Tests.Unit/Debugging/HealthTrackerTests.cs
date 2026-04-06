using McpProxy.Sdk.Debugging;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Debugging;

public class HealthTrackerTests
{
    private readonly ILogger<HealthTracker> _logger;
    private readonly HealthTracker _tracker;

    public HealthTrackerTests()
    {
        _logger = Substitute.For<ILogger<HealthTracker>>();
        _tracker = new HealthTracker(_logger);
    }

    public class GetHealthStatusAsyncTests : HealthTrackerTests
    {
        [Fact]
        public async Task GetHealthStatusAsync_InitialState_ReturnsUnknownWithZeroCounters()
        {
            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Status.Should().Be(HealthStatus.Unknown);
            status.TotalRequests.Should().Be(0);
            status.FailedRequests.Should().Be(0);
            status.ActiveConnections.Should().Be(0);
            status.Backends.Should().BeEmpty();
            status.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
            status.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task GetHealthStatusAsync_WithBackends_ReturnsBackendStatuses()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            _tracker.RecordConnectionState("backend2", connected: true);
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Backends.Should().HaveCount(2);
            status.Backends.Should().ContainKey("backend1");
            status.Backends.Should().ContainKey("backend2");
        }
    }

    public class RecordSuccessTests : HealthTrackerTests
    {
        [Fact]
        public async Task RecordSuccess_IncrementsCounters()
        {
            // Act
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);
            _tracker.RecordSuccess("backend1", responseTimeMs: 100);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.TotalRequests.Should().Be(2);
            status.FailedRequests.Should().Be(0);
            status.Backends["backend1"].TotalRequests.Should().Be(2);
            status.Backends["backend1"].FailedRequests.Should().Be(0);
        }

        [Fact]
        public async Task RecordSuccess_TracksAverageResponseTime()
        {
            // Act
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);
            _tracker.RecordSuccess("backend1", responseTimeMs: 100);
            _tracker.RecordSuccess("backend1", responseTimeMs: 150);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].AverageResponseTimeMs.Should().BeApproximately(100, 0.01);
        }

        [Fact]
        public async Task RecordSuccess_ResetsConsecutiveFailures()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            _tracker.RecordFailure("backend1", "error1");
            _tracker.RecordFailure("backend1", "error2");

            // Act
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].ConsecutiveFailures.Should().Be(0);
        }

        [Fact]
        public async Task RecordSuccess_UpdatesLastSuccessfulRequest()
        {
            // Act
            var beforeTime = DateTimeOffset.UtcNow;
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);
            var afterTime = DateTimeOffset.UtcNow;

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].LastSuccessfulRequest.Should().NotBeNull();
            status.Backends["backend1"].LastSuccessfulRequest.Should()
                .BeOnOrAfter(beforeTime)
                .And.BeOnOrBefore(afterTime);
        }
    }

    public class RecordFailureTests : HealthTrackerTests
    {
        [Fact]
        public async Task RecordFailure_IncrementsCounters()
        {
            // Act
            _tracker.RecordFailure("backend1", "Connection refused");
            _tracker.RecordFailure("backend1", "Timeout");

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.TotalRequests.Should().Be(2);
            status.FailedRequests.Should().Be(2);
            status.Backends["backend1"].TotalRequests.Should().Be(2);
            status.Backends["backend1"].FailedRequests.Should().Be(2);
        }

        [Fact]
        public async Task RecordFailure_IncrementsConsecutiveFailures()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);

            // Act
            _tracker.RecordFailure("backend1", "error1");
            _tracker.RecordFailure("backend1", "error2");
            _tracker.RecordFailure("backend1", "error3");

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].ConsecutiveFailures.Should().Be(3);
        }

        [Fact]
        public async Task RecordFailure_StoresLastError()
        {
            // Act
            _tracker.RecordFailure("backend1", "First error");
            _tracker.RecordFailure("backend1", "Second error");

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].LastError.Should().Be("Second error");
        }

        [Fact]
        public async Task RecordFailure_UpdatesLastFailedRequest()
        {
            // Act
            var beforeTime = DateTimeOffset.UtcNow;
            _tracker.RecordFailure("backend1", "error");
            var afterTime = DateTimeOffset.UtcNow;

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].LastFailedRequest.Should().NotBeNull();
            status.Backends["backend1"].LastFailedRequest.Should()
                .BeOnOrAfter(beforeTime)
                .And.BeOnOrBefore(afterTime);
        }
    }

    public class RecordConnectionStateTests : HealthTrackerTests
    {
        [Fact]
        public async Task RecordConnectionState_Connected_SetsConnectedTrue()
        {
            // Act
            _tracker.RecordConnectionState("backend1", connected: true);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].Connected.Should().BeTrue();
        }

        [Fact]
        public async Task RecordConnectionState_Disconnected_SetsConnectedFalse()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);

            // Act
            _tracker.RecordConnectionState("backend1", connected: false);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].Connected.Should().BeFalse();
        }

        [Fact]
        public async Task RecordConnectionState_Connected_ResetsConsecutiveFailures()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            _tracker.RecordFailure("backend1", "error1");
            _tracker.RecordFailure("backend1", "error2");

            // Act
            _tracker.RecordConnectionState("backend1", connected: true);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].ConsecutiveFailures.Should().Be(0);
        }
    }

    public class RecordCapabilitiesTests : HealthTrackerTests
    {
        [Fact]
        public async Task RecordCapabilities_StoresToolPromptResourceCounts()
        {
            // Act
            _tracker.RecordCapabilities("backend1", toolCount: 10, promptCount: 5, resourceCount: 3);

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.Backends["backend1"].ToolCount.Should().Be(10);
            status.Backends["backend1"].PromptCount.Should().Be(5);
            status.Backends["backend1"].ResourceCount.Should().Be(3);
        }
    }

    public class ActiveConnectionsTests : HealthTrackerTests
    {
        [Fact]
        public async Task IncrementActiveConnections_IncreasesCount()
        {
            // Act
            _tracker.IncrementActiveConnections();
            _tracker.IncrementActiveConnections();

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.ActiveConnections.Should().Be(2);
        }

        [Fact]
        public async Task DecrementActiveConnections_DecreasesCount()
        {
            // Arrange
            _tracker.IncrementActiveConnections();
            _tracker.IncrementActiveConnections();

            // Act
            _tracker.DecrementActiveConnections();

            // Assert
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);
            status.ActiveConnections.Should().Be(1);
        }
    }

    public class HealthStatusDeterminationTests : HealthTrackerTests
    {
        [Fact]
        public async Task Backend_Healthy_WhenConnectedWithNoFailures()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Backends["backend1"].Status.Should().Be(HealthStatus.Healthy);
            status.Status.Should().Be(HealthStatus.Healthy);
        }

        [Fact]
        public async Task Backend_Unknown_WhenNotConnected()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: false);

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Backends["backend1"].Status.Should().Be(HealthStatus.Unknown);
        }

        [Fact]
        public async Task Backend_Degraded_WhenTwoConsecutiveFailures()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            _tracker.RecordFailure("backend1", "error1");
            _tracker.RecordFailure("backend1", "error2");

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Backends["backend1"].Status.Should().Be(HealthStatus.Degraded);
        }

        [Fact]
        public async Task Backend_Unhealthy_WhenFiveConsecutiveFailures()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            for (int i = 0; i < 5; i++)
            {
                _tracker.RecordFailure("backend1", $"error{i}");
            }

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Backends["backend1"].Status.Should().Be(HealthStatus.Unhealthy);
        }

        [Fact]
        public async Task Overall_Degraded_WhenAnyBackendDegraded()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            _tracker.RecordSuccess("backend1", responseTimeMs: 50);

            _tracker.RecordConnectionState("backend2", connected: true);
            _tracker.RecordFailure("backend2", "error1");
            _tracker.RecordFailure("backend2", "error2");

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Backends["backend1"].Status.Should().Be(HealthStatus.Healthy);
            status.Backends["backend2"].Status.Should().Be(HealthStatus.Degraded);
            status.Status.Should().Be(HealthStatus.Degraded);
        }

        [Fact]
        public async Task Overall_Unhealthy_WhenAllBackendsUnhealthy()
        {
            // Arrange
            _tracker.RecordConnectionState("backend1", connected: true);
            for (int i = 0; i < 5; i++)
            {
                _tracker.RecordFailure("backend1", $"error{i}");
            }

            // Act
            var status = await _tracker.GetHealthStatusAsync(TestContext.Current.CancellationToken);

            // Assert
            status.Status.Should().Be(HealthStatus.Unhealthy);
        }
    }
}

public class NullHealthTrackerTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Act
        var instance1 = NullHealthTracker.Instance;
        var instance2 = NullHealthTracker.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public async Task GetHealthStatusAsync_ReturnsUnknownStatus()
    {
        // Act
        var status = await NullHealthTracker.Instance.GetHealthStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        status.Status.Should().Be(HealthStatus.Unknown);
        status.TotalRequests.Should().Be(0);
        status.FailedRequests.Should().Be(0);
        status.ActiveConnections.Should().Be(0);
        status.Backends.Should().BeEmpty();
    }

    [Fact]
    public void AllMethods_DoNotThrow()
    {
        // Arrange
        var tracker = NullHealthTracker.Instance;

        // Act & Assert
        var act = () =>
        {
            tracker.RecordSuccess("backend", 50);
            tracker.RecordFailure("backend", "error");
            tracker.RecordConnectionState("backend", true);
            tracker.RecordCapabilities("backend", 1, 2, 3);
            tracker.IncrementActiveConnections();
            tracker.DecrementActiveConnections();
        };

        act.Should().NotThrow();
    }
}
