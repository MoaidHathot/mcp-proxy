using McpProxy.Abstractions;
using McpProxy.Sdk.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class ContentFilterHookTests
{
    private readonly ILogger<ContentFilterHook> _logger;

    public ContentFilterHookTests()
    {
        _logger = Substitute.For<ILogger<ContentFilterHook>>();
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

    private static CallToolResult CreateResult(string text)
    {
        return new CallToolResult
        {
            IsError = false,
            Content = [new TextContentBlock { Text = text }]
        };
    }

    [Fact]
    public void Priority_ReturnsHighValue()
    {
        // Arrange
        var config = new ContentFilterConfiguration();
        var hook = new ContentFilterHook(_logger, config);

        // Assert - content filter should execute late
        hook.Priority.Should().BeGreaterThan(100);
    }

    [Fact]
    public async Task OnPostInvokeAsync_EmptyContent_ReturnsOriginal()
    {
        // Arrange
        var config = new ContentFilterConfiguration();
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = new CallToolResult { IsError = false, Content = [] };

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_NoPatternMatch_ReturnsOriginal()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns = [new ContentFilterPattern { Name = "test", Pattern = "forbidden" }]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("This is normal text");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_BlockMode_ReturnsError()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "blocked",
                    Pattern = "BLOCKED_CONTENT",
                    Mode = ContentFilterMode.Block
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("This has BLOCKED_CONTENT in it");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.IsError.Should().BeTrue();
        var textContent = returned.Content![0] as TextContentBlock;
        textContent!.Text.Should().Contain("blocked");
    }

    [Fact]
    public async Task OnPostInvokeAsync_RedactMode_RedactsContent()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "ssn",
                    Pattern = @"\d{3}-\d{2}-\d{4}",
                    Mode = ContentFilterMode.Redact,
                    RedactReplacement = "[SSN-REDACTED]"
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("My SSN is 123-45-6789");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.IsError.Should().BeFalse();
        var textContent = returned.Content![0] as TextContentBlock;
        textContent!.Text.Should().Be("My SSN is [SSN-REDACTED]");
    }

    [Fact]
    public async Task OnPostInvokeAsync_WarnMode_DoesNotModify()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "warning",
                    Pattern = "sensitive",
                    Mode = ContentFilterMode.Warn
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("This contains sensitive data");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_DefaultPatterns_DetectsCreditCard()
    {
        // Arrange
        var config = new ContentFilterConfiguration { UseDefaultPatterns = true };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("Card number: 4111-1111-1111-1111");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert - should be redacted (default patterns redact credit cards)
        var textContent = returned.Content![0] as TextContentBlock;
        textContent!.Text.Should().Contain("[CREDIT-CARD-REDACTED]");
    }

    [Fact]
    public async Task OnPostInvokeAsync_DefaultPatterns_BlocksPrivateKey()
    {
        // Arrange
        var config = new ContentFilterConfiguration { UseDefaultPatterns = true };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("-----BEGIN PRIVATE KEY-----");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert - should be blocked
        returned.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostInvokeAsync_AppliesTo_MatchingTool_Filters()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "limited",
                    Pattern = "secret",
                    Mode = ContentFilterMode.Block,
                    AppliesTo = ["special_*"]
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext(toolName: "special_tool");
        var result = CreateResult("This is a secret");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert - should be blocked
        returned.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostInvokeAsync_AppliesTo_NonMatchingTool_Skips()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "limited",
                    Pattern = "secret",
                    Mode = ContentFilterMode.Block,
                    AppliesTo = ["special_*"]
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext(toolName: "other_tool");
        var result = CreateResult("This is a secret");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert - should NOT be blocked since tool doesn't match
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public async Task OnPostInvokeAsync_MultipleRedactions_AppliesAll()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "ssn",
                    Pattern = @"\d{3}-\d{2}-\d{4}",
                    Mode = ContentFilterMode.Redact,
                    RedactReplacement = "[SSN]"
                },
                new ContentFilterPattern
                {
                    Name = "phone",
                    Pattern = @"\d{3}-\d{3}-\d{4}",
                    Mode = ContentFilterMode.Redact,
                    RedactReplacement = "[PHONE]"
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("SSN: 123-45-6789, Phone: 555-123-4567");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert
        var textContent = returned.Content![0] as TextContentBlock;
        textContent!.Text.Should().Contain("[SSN]");
        textContent.Text.Should().Contain("[PHONE]");
    }

    [Fact]
    public async Task OnPostInvokeAsync_DisabledPattern_Ignored()
    {
        // Arrange
        var config = new ContentFilterConfiguration
        {
            UseDefaultPatterns = false,
            Patterns =
            [
                new ContentFilterPattern
                {
                    Name = "disabled",
                    Pattern = "blocked",
                    Mode = ContentFilterMode.Block,
                    Enabled = false
                }
            ]
        };
        var hook = new ContentFilterHook(_logger, config);
        var context = CreateContext();
        var result = CreateResult("This is blocked content");

        // Act
        var returned = await hook.OnPostInvokeAsync(context, result);

        // Assert - disabled pattern should not block
        returned.Should().BeSameAs(result);
    }
}
