using System.Diagnostics;
using McpProxy.SDK.Telemetry;

namespace McpProxy.Tests.Unit.Telemetry;

public class ProxyActivitySourceTests : IDisposable
{
    private readonly ProxyActivitySource _activitySource;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _capturedActivities = [];

    public ProxyActivitySourceTests()
    {
        _activitySource = new ProxyActivitySource("1.0.0-test");
        
        // Set up a listener to capture activities
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ProxyActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _capturedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activitySource.Dispose();
    }

    [Fact]
    public void StartToolCall_CreatesActivityWithCorrectTags()
    {
        // Act
        using var activity = _activitySource.StartToolCall("test-server", "test-tool");

        // Assert
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("mcpproxy.tool_call");
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem("mcp.server").Should().Be("test-server");
        activity.GetTagItem("mcp.tool").Should().Be("test-tool");
        activity.GetTagItem("mcp.operation").Should().Be("tool_call");
    }

    [Fact]
    public void StartResourceRead_CreatesActivityWithCorrectTags()
    {
        // Act
        using var activity = _activitySource.StartResourceRead("test-server", "file:///test.txt");

        // Assert
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("mcpproxy.resource_read");
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem("mcp.server").Should().Be("test-server");
        activity.GetTagItem("mcp.resource").Should().Be("file:///test.txt");
        activity.GetTagItem("mcp.operation").Should().Be("resource_read");
    }

    [Fact]
    public void StartPromptGet_CreatesActivityWithCorrectTags()
    {
        // Act
        using var activity = _activitySource.StartPromptGet("test-server", "test-prompt");

        // Assert
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("mcpproxy.prompt_get");
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem("mcp.server").Should().Be("test-server");
        activity.GetTagItem("mcp.prompt").Should().Be("test-prompt");
        activity.GetTagItem("mcp.operation").Should().Be("prompt_get");
    }

    [Fact]
    public void StartListTools_CreatesActivityWithCorrectTags()
    {
        // Act
        using var activity = _activitySource.StartListTools();

        // Assert
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("mcpproxy.list_tools");
        activity.Kind.Should().Be(ActivityKind.Server);
        activity.GetTagItem("mcp.operation").Should().Be("list_tools");
    }

    [Fact]
    public void StartListResources_CreatesActivityWithCorrectTags()
    {
        // Act
        using var activity = _activitySource.StartListResources();

        // Assert
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("mcpproxy.list_resources");
        activity.Kind.Should().Be(ActivityKind.Server);
        activity.GetTagItem("mcp.operation").Should().Be("list_resources");
    }

    [Fact]
    public void StartListPrompts_CreatesActivityWithCorrectTags()
    {
        // Act
        using var activity = _activitySource.StartListPrompts();

        // Assert
        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("mcpproxy.list_prompts");
        activity.Kind.Should().Be(ActivityKind.Server);
        activity.GetTagItem("mcp.operation").Should().Be("list_prompts");
    }

    [Fact]
    public void RecordError_SetsErrorStatusAndTags()
    {
        // Arrange
        using var activity = _activitySource.StartToolCall("test-server", "test-tool");
        var exception = new InvalidOperationException("Test error");

        // Act
        ProxyActivitySource.RecordError(activity, exception);

        // Assert
        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Test error");
        activity.GetTagItem("error.type").Should().Be("InvalidOperationException");
        activity.GetTagItem("error.message").Should().Be("Test error");
    }

    [Fact]
    public void RecordSuccess_SetsOkStatus()
    {
        // Arrange
        using var activity = _activitySource.StartToolCall("test-server", "test-tool");

        // Act
        ProxyActivitySource.RecordSuccess(activity);

        // Assert
        activity!.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void RecordError_WithNullActivity_DoesNotThrow()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act & Assert - should not throw
        var act = () => ProxyActivitySource.RecordError(null, exception);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordSuccess_WithNullActivity_DoesNotThrow()
    {
        // Act & Assert - should not throw
        var act = () => ProxyActivitySource.RecordSuccess(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void SourceName_IsCorrect()
    {
        // Assert
        ProxyActivitySource.SourceName.Should().Be("McpProxy");
    }
}
