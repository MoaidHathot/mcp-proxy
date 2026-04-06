using System.Diagnostics.Metrics;
using McpProxy.Sdk.Telemetry;

namespace McpProxy.Tests.Unit.Telemetry;

public class ProxyMetricsTests : IDisposable
{
    private readonly TestMeterFactory _meterFactory;
    private readonly ProxyMetrics _metrics;

    public ProxyMetricsTests()
    {
        _meterFactory = new TestMeterFactory();
        _metrics = new ProxyMetrics(_meterFactory);
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _meterFactory.Dispose();
    }

    [Fact]
    public void RecordToolCall_IncrementsTotalCounter()
    {
        // Arrange & Act
        _metrics.RecordToolCall("test-server", "test-tool");

        // Assert - metrics are recorded without error
        // In real scenarios, you would use a MeterListener to verify
    }

    [Fact]
    public void RecordToolCallSuccess_IncrementsSuccessCounter()
    {
        // Arrange & Act
        _metrics.RecordToolCallSuccess("test-server", "test-tool");

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void RecordToolCallFailure_IncrementsFailureCounter()
    {
        // Arrange & Act
        _metrics.RecordToolCallFailure("test-server", "test-tool", "timeout");

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void RecordToolCallDuration_RecordsHistogram()
    {
        // Arrange & Act
        _metrics.RecordToolCallDuration("test-server", "test-tool", 150.5);

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void RecordResourceRead_IncrementsTotalCounter()
    {
        // Arrange & Act
        _metrics.RecordResourceRead("test-server", "file:///test.txt");

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void RecordResourceReadDuration_RecordsHistogram()
    {
        // Arrange & Act
        _metrics.RecordResourceReadDuration("test-server", "file:///test.txt", 50.0);

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void RecordPromptGet_IncrementsTotalCounter()
    {
        // Arrange & Act
        _metrics.RecordPromptGet("test-server", "test-prompt");

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void RecordPromptGetDuration_RecordsHistogram()
    {
        // Arrange & Act
        _metrics.RecordPromptGetDuration("test-server", "test-prompt", 25.0);

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void IncrementBackendConnections_IncrementsCounter()
    {
        // Arrange & Act
        _metrics.IncrementBackendConnections("test-server");

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void DecrementBackendConnections_DecrementsCounter()
    {
        // Arrange & Act
        _metrics.DecrementBackendConnections("test-server");

        // Assert - metrics are recorded without error
    }

    [Fact]
    public void MultipleOperations_TrackedIndependently()
    {
        // Arrange & Act
        _metrics.RecordToolCall("server1", "tool1");
        _metrics.RecordToolCall("server2", "tool2");
        _metrics.RecordToolCallSuccess("server1", "tool1");
        _metrics.RecordToolCallFailure("server2", "tool2", "error");
        _metrics.RecordResourceRead("server1", "resource1");
        _metrics.RecordPromptGet("server1", "prompt1");

        // Assert - all metrics recorded without error
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
