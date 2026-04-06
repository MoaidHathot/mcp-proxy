using McpProxy.SDK.Configuration;
using McpProxy.SDK.Debugging;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Debugging;

public class HookTracerTests
{
    private readonly ILogger<HookTracer> _logger;

    public HookTracerTests()
    {
        _logger = Substitute.For<ILogger<HookTracer>>();
    }

    private HookTracer CreateTracer(bool includeHookTiming = true)
    {
        var config = new DebugConfiguration
        {
            HookTracing = true,
            IncludeHookTiming = includeHookTiming
        };
        return new HookTracer(_logger, config);
    }

    public class BeginTraceTests : HookTracerTests
    {
        [Fact]
        public void BeginTrace_ReturnsContextWithCorrectProperties()
        {
            // Arrange
            var tracer = CreateTracer();

            // Act
            var context = tracer.BeginTrace("test_tool", "test-server");

            // Assert
            context.ToolName.Should().Be("test_tool");
            context.ServerName.Should().Be("test-server");
            context.Entries.Should().BeEmpty();
            context.StartTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        }
    }

    public class RecordHookStartTests : HookTracerTests
    {
        [Fact]
        public void RecordHookStart_AddsEntryToContext()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");

            // Act
            tracer.RecordHookStart(context, "TestHook", "PreInvoke", 10);

            // Assert
            context.Entries.Should().HaveCount(1);
            var entry = context.Entries[0];
            entry.HookName.Should().Be("TestHook");
            entry.HookType.Should().Be("PreInvoke");
            entry.Priority.Should().Be(10);
            entry.Status.Should().Be("Executing");
            entry.DurationMs.Should().BeNull();
            entry.Error.Should().BeNull();
        }

        [Fact]
        public void RecordHookStart_MultipleHooks_AddsMultipleEntries()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");

            // Act
            tracer.RecordHookStart(context, "Hook1", "PreInvoke", 1);
            tracer.RecordHookStart(context, "Hook2", "PreInvoke", 2);
            tracer.RecordHookStart(context, "Hook3", "PostInvoke", 3);

            // Assert
            context.Entries.Should().HaveCount(3);
        }
    }

    public class RecordHookCompleteTests : HookTracerTests
    {
        [Fact]
        public void RecordHookComplete_UpdatesEntryStatusAndDuration()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");
            tracer.RecordHookStart(context, "TestHook", "PreInvoke", 10);
            var duration = TimeSpan.FromMilliseconds(50);

            // Act
            tracer.RecordHookComplete(context, "TestHook", duration);

            // Assert
            context.Entries.Should().HaveCount(1);
            var entry = context.Entries[0];
            entry.Status.Should().Be("Completed");
            entry.DurationMs.Should().BeApproximately(50, 0.01);
        }

        [Fact]
        public void RecordHookComplete_NonExistingHook_DoesNotThrow()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");

            // Act
            var act = () => tracer.RecordHookComplete(context, "NonExistingHook", TimeSpan.FromMilliseconds(10));

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void RecordHookComplete_AlreadyCompletedHook_DoesNotUpdateAgain()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");
            tracer.RecordHookStart(context, "TestHook", "PreInvoke", 10);
            tracer.RecordHookComplete(context, "TestHook", TimeSpan.FromMilliseconds(50));

            // Act
            tracer.RecordHookComplete(context, "TestHook", TimeSpan.FromMilliseconds(100));

            // Assert - should still have the original duration
            var entry = context.Entries[0];
            entry.DurationMs.Should().BeApproximately(50, 0.01);
        }
    }

    public class RecordHookFailedTests : HookTracerTests
    {
        [Fact]
        public void RecordHookFailed_UpdatesEntryStatusAndError()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");
            tracer.RecordHookStart(context, "TestHook", "PreInvoke", 10);
            var exception = new InvalidOperationException("Something went wrong");

            // Act
            tracer.RecordHookFailed(context, "TestHook", exception);

            // Assert
            context.Entries.Should().HaveCount(1);
            var entry = context.Entries[0];
            entry.Status.Should().Be("Failed");
            entry.Error.Should().Be("Something went wrong");
        }

        [Fact]
        public void RecordHookFailed_NonExistingHook_DoesNotThrow()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");
            var exception = new InvalidOperationException("Error");

            // Act
            var act = () => tracer.RecordHookFailed(context, "NonExistingHook", exception);

            // Assert
            act.Should().NotThrow();
        }
    }

    public class EndTraceTests : HookTracerTests
    {
        [Fact]
        public void EndTrace_WithCompletedHooks_DoesNotThrow()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");
            tracer.RecordHookStart(context, "Hook1", "PreInvoke", 1);
            tracer.RecordHookComplete(context, "Hook1", TimeSpan.FromMilliseconds(10));
            tracer.RecordHookStart(context, "Hook2", "PostInvoke", 2);
            tracer.RecordHookComplete(context, "Hook2", TimeSpan.FromMilliseconds(20));

            // Act
            var act = () => tracer.EndTrace(context);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void EndTrace_WithMixedResults_DoesNotThrow()
        {
            // Arrange
            var tracer = CreateTracer();
            var context = tracer.BeginTrace("test_tool", "test-server");
            tracer.RecordHookStart(context, "Hook1", "PreInvoke", 1);
            tracer.RecordHookComplete(context, "Hook1", TimeSpan.FromMilliseconds(10));
            tracer.RecordHookStart(context, "Hook2", "PreInvoke", 2);
            tracer.RecordHookFailed(context, "Hook2", new Exception("Failed"));

            // Act
            var act = () => tracer.EndTrace(context);

            // Assert
            act.Should().NotThrow();
        }
    }
}

public class NullHookTracerTests
{
    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Act
        var instance1 = NullHookTracer.Instance;
        var instance2 = NullHookTracer.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void BeginTrace_ReturnsEmptyContext()
    {
        // Act
        var context = NullHookTracer.Instance.BeginTrace("tool", "server");

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void AllMethods_DoNotThrow()
    {
        // Arrange
        var tracer = NullHookTracer.Instance;
        var context = tracer.BeginTrace("tool", "server");

        // Act & Assert
        var act = () =>
        {
            tracer.RecordHookStart(context, "hook", "PreInvoke", 1);
            tracer.RecordHookComplete(context, "hook", TimeSpan.FromMilliseconds(10));
            tracer.RecordHookFailed(context, "hook2", new Exception("error"));
            tracer.EndTrace(context);
        };

        act.Should().NotThrow();
    }
}
