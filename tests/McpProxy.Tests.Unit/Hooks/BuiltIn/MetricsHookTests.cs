using System.Diagnostics;
using System.Diagnostics.Metrics;
using McpProxy.Abstractions;
using McpProxy.Sdk.Hooks.BuiltIn;
using McpProxy.Sdk.Telemetry;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class MetricsHookTests : IDisposable
{
    private readonly ILogger<MetricsHook> _logger;
    private readonly ProxyMetrics _metrics;
    private readonly TestMeterFactory _meterFactory;

    public MetricsHookTests()
    {
        _logger = Substitute.For<ILogger<MetricsHook>>();
        _meterFactory = new TestMeterFactory();
        _metrics = new ProxyMetrics(_meterFactory);
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static HookContext<CallToolRequestParams> CreateContext(string toolName = "test_tool")
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "test-server",
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = TestContext.Current.CancellationToken
        };
    }

    private static CallToolResult CreateSuccessResult()
    {
        return new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = "Success" }]
        };
    }

    private static CallToolResult CreateErrorResult(string errorMessage = "error")
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = errorMessage }]
        };
    }

    [Fact]
    public void Priority_ReturnsHighPositiveValue()
    {
        // Arrange
        var config = new MetricsHookConfiguration();
        var hook = new MetricsHook(_logger, _metrics, config);

        // Assert - metrics should execute late to capture accurate timing
        hook.Priority.Should().BeGreaterThan(100);
        hook.Priority.Should().Be(900);
    }

    [Fact]
    public async Task OnPreInvokeAsync_Completes()
    {
        // Arrange
        var config = new MetricsHookConfiguration();
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext("my_tool");

        // Act & Assert - should complete without error
        await hook.OnPreInvokeAsync(context);
    }

    [Fact]
    public async Task OnPreInvokeAsync_RecordTiming_StoresStopwatch()
    {
        // Arrange
        var config = new MetricsHookConfiguration { RecordTiming = true };
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey(MetricsHook.StopwatchKey);
        context.Items[MetricsHook.StopwatchKey].Should().BeOfType<Stopwatch>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_RecordTimingFalse_NoStopwatch()
    {
        // Arrange
        var config = new MetricsHookConfiguration { RecordTiming = false };
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().NotContainKey(MetricsHook.StopwatchKey);
    }

    [Fact]
    public async Task OnPreInvokeAsync_RecordSizes_StoresRequestSize()
    {
        // Arrange
        var config = new MetricsHookConfiguration { RecordSizes = true };
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey(MetricsHook.RequestSizeKey);
        context.Items[MetricsHook.RequestSizeKey].Should().BeOfType<int>();
    }

    [Fact]
    public async Task OnPostInvokeAsync_SuccessResult_ReturnsOriginal()
    {
        // Arrange
        var config = new MetricsHookConfiguration();
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();
        var result = CreateSuccessResult();

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_ErrorResult_ReturnsOriginal()
    {
        // Arrange
        var config = new MetricsHookConfiguration();
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();
        var result = CreateErrorResult("timeout occurred");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_WithStopwatch_RecordsDurationWithoutError()
    {
        // Arrange
        var config = new MetricsHookConfiguration { RecordTiming = true };
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();

        // Simulate pre-invoke setting up stopwatch
        await hook.OnPreInvokeAsync(context);

        // Simulate some delay
        await Task.Delay(10, TestContext.Current.CancellationToken);

        var result = CreateSuccessResult();

        // Act & Assert - should complete without error
        var returned = await hook.OnPostInvokeAsync(context, result);
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_ErrorTypeDetection_ProcessesVariousErrors()
    {
        // Arrange
        var config = new MetricsHookConfiguration();
        var hook = new MetricsHook(_logger, _metrics, config);

        // Test various error types
        var errorTypes = new[]
        {
            "connection refused by server",
            "unauthorized access",
            "rate limit exceeded",
            "generic error"
        };

        foreach (var errorMessage in errorTypes)
        {
            var context = CreateContext();
            var result = CreateErrorResult(errorMessage);

            // Act & Assert - should complete without error
            var returned = await hook.OnPostInvokeAsync(context, result);
            returned.Should().BeSameAs(result);
        }
    }

    [Fact]
    public async Task OnPostInvokeAsync_WithSizes_RecordsResponseSize()
    {
        // Arrange
        var config = new MetricsHookConfiguration { RecordSizes = true };
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext();
        await hook.OnPreInvokeAsync(context);

        var result = CreateSuccessResult();

        // Act & Assert - should complete without error
        var returned = await hook.OnPostInvokeAsync(context, result);
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task FullLifecycle_PreAndPostInvoke_CompletesSuccessfully()
    {
        // Arrange
        var config = new MetricsHookConfiguration
        {
            RecordTiming = true,
            RecordSizes = true
        };
        var hook = new MetricsHook(_logger, _metrics, config);
        var context = CreateContext("lifecycle_tool");

        // Act - simulate full lifecycle
        await hook.OnPreInvokeAsync(context);

        context.Items.Should().ContainKey(MetricsHook.StopwatchKey);
        context.Items.Should().ContainKey(MetricsHook.RequestSizeKey);

        await Task.Delay(5, TestContext.Current.CancellationToken);
        var result = CreateSuccessResult();

        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
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
