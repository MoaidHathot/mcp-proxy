using McpProxy.Abstractions;
using McpProxy.Core.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class RetryHookTests
{
    private readonly ILogger<RetryHook> _logger;

    public RetryHookTests()
    {
        _logger = Substitute.For<ILogger<RetryHook>>();
    }

    private static HookContext<CallToolRequestParams> CreateContext(string toolName = "test_tool")
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "test-server",
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = CancellationToken.None
        };
    }

    private static CallToolResult CreateErrorResult(string errorMessage = "An error occurred")
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = errorMessage }]
        };
    }

    private static CallToolResult CreateSuccessResult(string message = "Success")
    {
        return new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = message }]
        };
    }

    [Fact]
    public void Priority_ReturnsPositiveValue()
    {
        // Arrange
        var config = new RetryConfiguration();
        var hook = new RetryHook(_logger, config);

        // Assert - retry should execute after tool call
        hook.Priority.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OnPostInvokeAsync_SuccessResult_NoRetryRequested()
    {
        // Arrange
        var config = new RetryConfiguration();
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        var result = CreateSuccessResult();

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().NotContainKey(RetryHook.RetryRequestedKey);
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_ErrorWithRetryablePattern_RequestsRetry()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 3,
            RetryablePatterns = ["timeout", "connection"]
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        var result = CreateErrorResult("Connection timeout occurred");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().ContainKey(RetryHook.RetryRequestedKey);
        context.Items[RetryHook.RetryRequestedKey].Should().Be(true);
    }

    [Fact]
    public async Task OnPostInvokeAsync_ErrorWithNonRetryablePattern_NoRetry()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 3,
            RetryablePatterns = ["timeout"],
            NonRetryablePatterns = ["invalid", "unauthorized"]
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        var result = CreateErrorResult("Invalid request format");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().NotContainKey(RetryHook.RetryRequestedKey);
    }

    [Fact]
    public async Task OnPostInvokeAsync_MaxRetriesExceeded_NoRetry()
    {
        // Arrange
        var config = new RetryConfiguration { MaxRetries = 3 };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        context.Items[RetryHook.RetryAttemptKey] = 3; // Already at max
        var result = CreateErrorResult("timeout error");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().NotContainKey(RetryHook.RetryRequestedKey);
    }

    [Fact]
    public async Task OnPostInvokeAsync_IncrementsAttemptCounter()
    {
        // Arrange
        var config = new RetryConfiguration { MaxRetries = 3 };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        context.Items[RetryHook.RetryAttemptKey] = 1;
        var result = CreateErrorResult("timeout error");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items[RetryHook.RetryAttemptKey].Should().Be(2);
    }

    [Fact]
    public async Task OnPostInvokeAsync_SetsRetryDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 3,
            InitialDelayMs = 100
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        var result = CreateErrorResult("timeout error");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().ContainKey(RetryHook.RetryDelayMsKey);
        var delay = (int)context.Items[RetryHook.RetryDelayMsKey]!;
        delay.Should().BeGreaterThanOrEqualTo(75); // InitialDelay * 0.75 (min jitter)
    }

    [Fact]
    public async Task OnPostInvokeAsync_ExponentialBackoff_IncreaseDelay()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 5,
            InitialDelayMs = 100,
            BackoffMultiplier = 2.0,
            UseJitter = false
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        context.Items[RetryHook.RetryAttemptKey] = 2; // Third attempt
        var result = CreateErrorResult("timeout error");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        var delay = (int)context.Items[RetryHook.RetryDelayMsKey]!;
        // InitialDelay * (Multiplier ^ attempt) = 100 * 2^2 = 400
        delay.Should().Be(400);
    }

    [Fact]
    public async Task OnPostInvokeAsync_DelayCapAtMax()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 10,
            InitialDelayMs = 1000,
            BackoffMultiplier = 10.0,
            MaxDelayMs = 5000,
            UseJitter = false
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        context.Items[RetryHook.RetryAttemptKey] = 5; // Would be huge without cap
        var result = CreateErrorResult("timeout error");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        var delay = (int)context.Items[RetryHook.RetryDelayMsKey]!;
        delay.Should().Be(config.MaxDelayMs);
    }

    [Fact]
    public async Task OnPostInvokeAsync_NoPatterns_RetriesAllErrors()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 3,
            RetryablePatterns = [] // Empty = retry all
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        var result = CreateErrorResult("Some random error");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().ContainKey(RetryHook.RetryRequestedKey);
    }

    [Fact]
    public async Task OnPostInvokeAsync_SetsMatchedPattern()
    {
        // Arrange
        var config = new RetryConfiguration
        {
            MaxRetries = 3,
            RetryablePatterns = ["timeout", "connection"]
        };
        var hook = new RetryHook(_logger, config);
        var context = CreateContext();
        var result = CreateErrorResult("connection refused");

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        context.Items.Should().ContainKey(RetryHook.RetryPatternKey);
        context.Items[RetryHook.RetryPatternKey].Should().Be("connection");
    }
}
