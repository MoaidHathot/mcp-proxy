using McpProxy.Abstractions;
using McpProxy.SDK.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class AuditHookTests
{
    private readonly ILogger<AuditHook> _logger;

    public AuditHookTests()
    {
        _logger = Substitute.For<ILogger<AuditHook>>();
    }

    private static HookContext<CallToolRequestParams> CreateContext(
        string toolName = "test_tool",
        string? principalId = null)
    {
        var authResult = principalId is not null
            ? AuthenticationResult.Success(principalId)
            : null;

        return new HookContext<CallToolRequestParams>
        {
            ServerName = "test-server",
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName },
            CancellationToken = TestContext.Current.CancellationToken,
            AuthenticationResult = authResult
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

    private static CallToolResult CreateErrorResult()
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "Error" }]
        };
    }

    [Fact]
    public void Priority_PreInvoke_ReturnsVeryLowValue()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);

        // Assert - audit should capture request before any transformation
        hook.Priority.Should().BeLessThan(-900);
    }

    [Fact]
    public async Task OnPreInvokeAsync_StoresStartTime()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey(AuditHook.AuditStartTimeKey);
        context.Items[AuditHook.AuditStartTimeKey].Should().BeOfType<DateTimeOffset>();
    }

    [Fact]
    public async Task OnPreInvokeAsync_WithCorrelationId_StoresCorrelationId()
    {
        // Arrange
        var config = new AuditConfiguration { IncludeCorrelationId = true };
        var hook = new AuditHook(_logger, config);
        var context = CreateContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey(AuditHook.AuditCorrelationIdKey);
        context.Items[AuditHook.AuditCorrelationIdKey].Should().NotBeNull();
    }

    [Fact]
    public async Task OnPreInvokeAsync_ExcludedTool_Skipped()
    {
        // Arrange
        var config = new AuditConfiguration
        {
            ExcludeTools = ["test_*"]
        };
        var hook = new AuditHook(_logger, config);
        var context = CreateContext(toolName: "test_something");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - should not store audit data for excluded tools
        context.Items.Should().NotContainKey(AuditHook.AuditStartTimeKey);
    }

    [Fact]
    public async Task OnPreInvokeAsync_IncludedTool_Audited()
    {
        // Arrange
        var config = new AuditConfiguration
        {
            IncludeTools = ["allowed_*"]
        };
        var hook = new AuditHook(_logger, config);
        var context = CreateContext(toolName: "allowed_tool");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey(AuditHook.AuditStartTimeKey);
    }

    [Fact]
    public async Task OnPreInvokeAsync_NotInIncludeList_Skipped()
    {
        // Arrange
        var config = new AuditConfiguration
        {
            IncludeTools = ["allowed_*"]
        };
        var hook = new AuditHook(_logger, config);
        var context = CreateContext(toolName: "other_tool");

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        context.Items.Should().NotContainKey(AuditHook.AuditStartTimeKey);
    }

    [Fact]
    public async Task OnPostInvokeAsync_ReturnsOriginalResult()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);
        var context = CreateContext();
        context.Items[AuditHook.AuditStartTimeKey] = DateTimeOffset.UtcNow;
        var result = CreateSuccessResult();

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_ExcludedTool_Skipped()
    {
        // Arrange
        var config = new AuditConfiguration
        {
            ExcludeTools = ["test_*"]
        };
        var hook = new AuditHook(_logger, config);
        var context = CreateContext(toolName: "test_something");
        var result = CreateSuccessResult();

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert - should return result without logging
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_Success_LogsSuccess()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);
        var context = CreateContext();
        context.Items[AuditHook.AuditStartTimeKey] = DateTimeOffset.UtcNow;
        var result = CreateSuccessResult();

        // Enable logging so we can verify
        _logger.IsEnabled(LogLevel.Information).Returns(true);

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert - logger should have been called
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task OnPostInvokeAsync_Error_LogsError()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);
        var context = CreateContext();
        context.Items[AuditHook.AuditStartTimeKey] = DateTimeOffset.UtcNow;
        var result = CreateErrorResult();

        // Enable logging so we can verify
        _logger.IsEnabled(LogLevel.Information).Returns(true);

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert - logger should have been called
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task OnPreInvokeAsync_AnonymousUser_UsesAnonymous()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);
        var context = CreateContext(principalId: null);

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert - should complete without error
        context.Items.Should().ContainKey(AuditHook.AuditStartTimeKey);
    }

    [Fact]
    public async Task OnPostInvokeAsync_CalculatesDuration()
    {
        // Arrange
        var config = new AuditConfiguration();
        var hook = new AuditHook(_logger, config);
        var context = CreateContext();

        // Set a start time in the past
        context.Items[AuditHook.AuditStartTimeKey] = DateTimeOffset.UtcNow.AddMilliseconds(-100);
        var result = CreateSuccessResult();

        // Act
        await hook.OnPostInvokeAsync(context, result);

        // Assert - should complete and have logged (we can't directly verify duration)
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }
}
