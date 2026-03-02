using McpProxy.Abstractions;
using McpProxy.Core.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class TimeoutHookTests
{
    private readonly ILogger<TimeoutHook> _logger;

    public TimeoutHookTests()
    {
        _logger = Substitute.For<ILogger<TimeoutHook>>();
    }

    private static HookContext<CallToolRequestParams> CreateContext(
        string toolName = "test_tool",
        CancellationToken cancellationToken = default)
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "test-server",
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = cancellationToken
        };
    }

    [Fact]
    public void Priority_ReturnsNegativeValue()
    {
        // Arrange
        var config = new TimeoutConfiguration();
        var hook = new TimeoutHook(_logger, config);

        // Assert - timeout should execute before tool call
        hook.Priority.Should().BeLessThan(0);
        hook.Priority.Should().Be(-800);
    }

    [Fact]
    public async Task OnPreInvokeAsync_SetsTimeoutCancellationToken()
    {
        // Arrange
        var config = new TimeoutConfiguration { DefaultTimeoutSeconds = 5 };
        var hook = new TimeoutHook(_logger, config);
        var originalToken = CancellationToken.None;
        var context = CreateContext(cancellationToken: originalToken);

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - context should have a new cancellation token
        context.CancellationToken.Should().NotBe(originalToken);
        context.CancellationToken.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task OnPreInvokeAsync_StoresLinkedTokenSource()
    {
        // Arrange
        var config = new TimeoutConfiguration { DefaultTimeoutSeconds = 5 };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - linked token source should be stored for disposal
        context.Items.Should().ContainKey(TimeoutHook.TimeoutCtsKey);
        context.Items[TimeoutHook.TimeoutCtsKey].Should().BeOfType<CancellationTokenSource>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_RespectsOriginalCancellation()
    {
        // Arrange
        var config = new TimeoutConfiguration { DefaultTimeoutSeconds = 60 }; // 1 minute timeout
        var hook = new TimeoutHook(_logger, config);
        using var originalCts = new CancellationTokenSource();
        var context = CreateContext(cancellationToken: originalCts.Token);

        // Act
        await hook.OnPreInvokeAsync(context);

        // Cancel the original token
        originalCts.Cancel();

        // Assert - the linked token should also be cancelled
        context.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task OnPreInvokeAsync_TimeoutCancelsToken()
    {
        // Arrange - use 1 second timeout (the minimum practical value in seconds)
        var config = new TimeoutConfiguration { DefaultTimeoutSeconds = 1 };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Wait for timeout to trigger (slightly more than 1 second)
        await Task.Delay(1200);

        // Assert - the token should be cancelled due to timeout
        context.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task OnPreInvokeAsync_PerToolTimeout_ExactMatch()
    {
        // Arrange
        var config = new TimeoutConfiguration
        {
            DefaultTimeoutSeconds = 30,
            PerTool = new Dictionary<string, int>
            {
                ["slow_tool"] = 120
            }
        };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext(toolName: "slow_tool");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - should use per-tool timeout
        context.Items.Should().ContainKey(TimeoutHook.TimeoutCtsKey);
        var cts = (CancellationTokenSource)context.Items[TimeoutHook.TimeoutCtsKey]!;
        cts.Token.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task OnPreInvokeAsync_PerToolTimeout_WildcardPrefix()
    {
        // Arrange
        var config = new TimeoutConfiguration
        {
            DefaultTimeoutSeconds = 30,
            PerTool = new Dictionary<string, int>
            {
                ["slow_*"] = 120
            }
        };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext(toolName: "slow_operation");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - should use wildcard-matched timeout
        context.Items.Should().ContainKey(TimeoutHook.TimeoutCtsKey);
    }

    [Fact]
    public async Task OnPreInvokeAsync_PerToolTimeout_WildcardSuffix()
    {
        // Arrange
        var config = new TimeoutConfiguration
        {
            DefaultTimeoutSeconds = 30,
            PerTool = new Dictionary<string, int>
            {
                ["*_operation"] = 120
            }
        };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext(toolName: "slow_operation");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - should use wildcard-matched timeout
        context.Items.Should().ContainKey(TimeoutHook.TimeoutCtsKey);
    }

    [Fact]
    public async Task OnPreInvokeAsync_DefaultTimeout_WhenNoPerToolMatch()
    {
        // Arrange
        var config = new TimeoutConfiguration
        {
            DefaultTimeoutSeconds = 30,
            PerTool = new Dictionary<string, int>
            {
                ["other_tool"] = 120
            }
        };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext(toolName: "my_tool");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - should use default timeout
        context.Items.Should().ContainKey(TimeoutHook.TimeoutCtsKey);
        var cts = (CancellationTokenSource)context.Items[TimeoutHook.TimeoutCtsKey]!;
        cts.Token.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task OnPreInvokeAsync_EmptyPerTool_UsesDefault()
    {
        // Arrange
        var config = new TimeoutConfiguration
        {
            DefaultTimeoutSeconds = 30
        };
        var hook = new TimeoutHook(_logger, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey(TimeoutHook.TimeoutCtsKey);
    }
}
