using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Hooks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007

namespace McpProxy.Tests.E2E;

public class TeamsMessageDefaultsHookTests
{
    private readonly TeamsMessageDefaultsHook _hook;

    public TeamsMessageDefaultsHookTests()
    {
        var logger = Substitute.For<ILogger<TeamsMessageDefaultsHook>>();
        _hook = new TeamsMessageDefaultsHook(logger);
    }

    private static HookContext<CallToolRequestParams> CreateContext(
        string toolName,
        Dictionary<string, JsonElement>? arguments = null)
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "teams-server",
            ToolName = toolName,
            Request = new CallToolRequestParams
            {
                Name = toolName,
                Arguments = arguments
            },
            CancellationToken = TestContext.Current.CancellationToken
        };
    }

    public class AddsContentTypeTests : TeamsMessageDefaultsHookTests
    {
        [Theory]
        [InlineData("PostMessage")]
        [InlineData("SendChatMessage")]
        [InlineData("PostChannelMessage")]
        [InlineData("SendChannelMessage")]
        [InlineData("ReplyToMessage")]
        [InlineData("ReplyToChannelMessage")]
        public async Task AddsContentTypeHtml_WhenMissing_ForKnownTools(string toolName)
        {
            // Arrange
            var args = new Dictionary<string, JsonElement>
            {
                ["chatId"] = JsonSerializer.SerializeToElement("some-chat-id"),
                ["body"] = JsonSerializer.SerializeToElement("<b>Hello</b>")
            };
            var context = CreateContext(toolName, args);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().ContainKey("contentType");
            context.Request.Arguments!["contentType"].GetString().Should().Be("html");
        }

        [Theory]
        [InlineData("teams_PostMessage")]
        [InlineData("teams_SendChatMessage")]
        [InlineData("teams_ReplyToChannelMessage")]
        public async Task AddsContentTypeHtml_ForPrefixedVariants(string toolName)
        {
            // Arrange
            var context = CreateContext(toolName);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().ContainKey("contentType");
            context.Request.Arguments!["contentType"].GetString().Should().Be("html");
        }

        [Fact]
        public async Task AddsContentTypeHtml_WhenArgumentsAreNull()
        {
            // Arrange
            var context = CreateContext("PostMessage", arguments: null);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().NotBeNull();
            context.Request.Arguments.Should().ContainKey("contentType");
            context.Request.Arguments!["contentType"].GetString().Should().Be("html");
        }

        [Fact]
        public async Task PreservesExistingArguments_WhenAddingContentType()
        {
            // Arrange
            var args = new Dictionary<string, JsonElement>
            {
                ["chatId"] = JsonSerializer.SerializeToElement("chat-123"),
                ["body"] = JsonSerializer.SerializeToElement("Hello")
            };
            var context = CreateContext("SendChatMessage", args);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().HaveCount(3);
            context.Request.Arguments!["chatId"].GetString().Should().Be("chat-123");
            context.Request.Arguments["body"].GetString().Should().Be("Hello");
            context.Request.Arguments["contentType"].GetString().Should().Be("html");
        }
    }

    public class SkipsContentTypeTests : TeamsMessageDefaultsHookTests
    {
        [Fact]
        public async Task SkipsContentType_WhenAlreadySet()
        {
            // Arrange
            var args = new Dictionary<string, JsonElement>
            {
                ["contentType"] = JsonSerializer.SerializeToElement("text"),
                ["body"] = JsonSerializer.SerializeToElement("Plain text")
            };
            var context = CreateContext("PostMessage", args);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments!["contentType"].GetString().Should().Be("text");
        }

        [Fact]
        public async Task SkipsContentType_WhenSnakeCaseVariantIsSet()
        {
            // Arrange
            var args = new Dictionary<string, JsonElement>
            {
                ["content_type"] = JsonSerializer.SerializeToElement("text"),
                ["body"] = JsonSerializer.SerializeToElement("Plain text")
            };
            var context = CreateContext("PostMessage", args);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert - should NOT have added contentType since content_type was present
            context.Request.Arguments.Should().NotContainKey("contentType");
            context.Request.Arguments!["content_type"].GetString().Should().Be("text");
        }

        [Fact]
        public async Task DoesNotModify_NonMessageTools()
        {
            // Arrange
            var args = new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("search term")
            };
            var context = CreateContext("ListChats", args);
            var originalRequest = context.Request;

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert - request should be unchanged (same reference)
            context.Request.Should().BeSameAs(originalRequest);
            context.Request.Arguments.Should().NotContainKey("contentType");
        }

        [Theory]
        [InlineData("ListChats")]
        [InlineData("GetChatById")]
        [InlineData("SearchMessages")]
        [InlineData("GetTeamMembers")]
        [InlineData("ListChannels")]
        public async Task DoesNotModify_VariousNonMessageTools(string toolName)
        {
            // Arrange
            var context = CreateContext(toolName);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().BeNull();
        }
    }

    public class CustomContentTypeTests : TeamsMessageDefaultsHookTests
    {
        [Fact]
        public async Task UsesCustomDefaultContentType()
        {
            // Arrange
            var logger = Substitute.For<ILogger<TeamsMessageDefaultsHook>>();
            var customHook = new TeamsMessageDefaultsHook(logger, defaultContentType: "markdown");
            var context = CreateContext("PostMessage");

            // Act
            await customHook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().ContainKey("contentType");
            context.Request.Arguments!["contentType"].GetString().Should().Be("markdown");
        }
    }

    public class PriorityTests : TeamsMessageDefaultsHookTests
    {
        [Fact]
        public void Priority_Is200()
        {
            // Assert - priority 200 means it runs after pagination (100) but before credential scan (300+)
            _hook.Priority.Should().Be(200);
        }
    }

    public class SuffixMatchTests : TeamsMessageDefaultsHookTests
    {
        [Theory]
        [InlineData("msgraph_PostMessage")]
        [InlineData("graph_SendChatMessage")]
        [InlineData("custom_ReplyToMessage")]
        public async Task MatchesSuffixForOtherPrefixes(string toolName)
        {
            // Arrange
            var context = CreateContext(toolName);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert - should match via suffix fallback
            context.Request.Arguments.Should().ContainKey("contentType");
            context.Request.Arguments!["contentType"].GetString().Should().Be("html");
        }

        [Theory]
        [InlineData("postmessage")]
        [InlineData("POSTMESSAGE")]
        [InlineData("Teams_POSTMESSAGE")]
        public async Task MatchesCaseInsensitively(string toolName)
        {
            // Arrange
            var context = CreateContext(toolName);

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert
            context.Request.Arguments.Should().ContainKey("contentType");
            context.Request.Arguments!["contentType"].GetString().Should().Be("html");
        }
    }
}
