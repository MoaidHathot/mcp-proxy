using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Samples.TeamsIntegration.Hooks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007

namespace McpProxy.Tests.E2E;

public class TeamsPaginationHookTests
{
    private readonly TeamsPaginationHook _hook;

    public TeamsPaginationHookTests()
    {
        var logger = Substitute.For<ILogger<TeamsPaginationHook>>();
        _hook = new TeamsPaginationHook(logger, defaultTop: 20);
    }

    // A list tool whose backend schema uses the OData `top` page-size knob.
    private static JsonElement TopSchema() => JsonDocument.Parse(
        """{"type":"object","properties":{"top":{"type":"integer"},"filter":{"type":"string"}}}""").RootElement;

    // A list tool whose backend schema uses a boolean `fetchAllPages` (the current
    // ListChats shape: memberUpns, topic, fetchAllPages — and crucially NO `top`).
    private static JsonElement FetchAllPagesSchema() => JsonDocument.Parse(
        """{"type":"object","properties":{"memberUpns":{"type":"array"},"topic":{"type":"string"},"fetchAllPages":{"type":"boolean"}}}""").RootElement;

    // A tool that declares neither pagination knob.
    private static JsonElement NeitherSchema() => JsonDocument.Parse(
        """{"type":"object","properties":{"chatId":{"type":"string"}}}""").RootElement;

    private static HookContext<CallToolRequestParams> CreateContext(
        string toolName,
        Dictionary<string, JsonElement>? arguments = null,
        JsonElement? inputSchema = null)
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
            CancellationToken = TestContext.Current.CancellationToken,
            ToolInputSchema = inputSchema
        };
    }

    // ── Schema declares fetchAllPages (no top): the ListChats regression ──────────

    public class FetchAllPagesSchemaTests : TeamsPaginationHookTests
    {
        [Fact]
        public async Task DoesNotInjectTop_WhenToolDoesNotDeclareIt()
        {
            // Arrange — mirrors the live ListChats schema that rejected an injected `top`.
            var context = CreateContext("ListChats", arguments: null, inputSchema: FetchAllPagesSchema());

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert — no bogus `top`; fetchAllPages defaulted to false to bound the result.
            context.Request.Arguments.Should().NotContainKey("top");
            context.Request.Arguments.Should().ContainKey("fetchAllPages");
            context.Request.Arguments!["fetchAllPages"].GetBoolean().Should().BeFalse();
        }

        [Fact]
        public async Task StripsCallerSuppliedTop_WhenToolDoesNotDeclareIt()
        {
            // Arrange — an LLM following stale guidance passes top=20 to ListChats.
            var members = new[] { "alice@contoso.com" };
            var args = new Dictionary<string, JsonElement>
            {
                ["memberUpns"] = JsonSerializer.SerializeToElement(members),
                ["top"] = JsonSerializer.SerializeToElement(20)
            };
            var context = CreateContext("ListChats", args, FetchAllPagesSchema());

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert — the unsupported `top` is removed so the backend won't reject the call.
            context.Request.Arguments.Should().NotContainKey("top");
            context.Request.Arguments.Should().ContainKey("memberUpns");
            context.Request.Arguments!["fetchAllPages"].GetBoolean().Should().BeFalse();
        }

        [Fact]
        public async Task RespectsExplicitFetchAllPagesTrue()
        {
            // Arrange — caller deliberately wants every page (e.g. a cache refresh).
            var args = new Dictionary<string, JsonElement>
            {
                ["fetchAllPages"] = JsonSerializer.SerializeToElement(true)
            };
            var context = CreateContext("ListChats", args, FetchAllPagesSchema());

            // Act
            await _hook.OnPreInvokeAsync(context);

            // Assert — do not override an explicit choice.
            context.Request.Arguments!["fetchAllPages"].GetBoolean().Should().BeTrue();
        }

        [Fact]
        public async Task AppliesToPrefixedToolName()
        {
            var context = CreateContext("teams_ListChats", arguments: null, inputSchema: FetchAllPagesSchema());

            await _hook.OnPreInvokeAsync(context);

            context.Request.Arguments.Should().NotContainKey("top");
            context.Request.Arguments!["fetchAllPages"].GetBoolean().Should().BeFalse();
        }
    }

    // ── Schema declares top: classic OData page-size tools keep working ───────────

    public class TopSchemaTests : TeamsPaginationHookTests
    {
        [Fact]
        public async Task InjectsDefaultTop_WhenAbsentAndToolDeclaresTop()
        {
            var context = CreateContext("ListChatMessages", arguments: null, inputSchema: TopSchema());

            await _hook.OnPreInvokeAsync(context);

            context.Request.Arguments.Should().ContainKey("top");
            context.Request.Arguments!["top"].GetInt32().Should().Be(20);
        }

        [Fact]
        public async Task CapsTop_WhenAboveDefault()
        {
            var args = new Dictionary<string, JsonElement> { ["top"] = JsonSerializer.SerializeToElement(500) };
            var context = CreateContext("ListChatMessages", args, TopSchema());

            await _hook.OnPreInvokeAsync(context);

            context.Request.Arguments!["top"].GetInt32().Should().Be(20);
        }

        [Fact]
        public async Task LeavesTop_WhenWithinLimit()
        {
            var args = new Dictionary<string, JsonElement> { ["top"] = JsonSerializer.SerializeToElement(10) };
            var context = CreateContext("ListChatMessages", args, TopSchema());

            await _hook.OnPreInvokeAsync(context);

            context.Request.Arguments!["top"].GetInt32().Should().Be(10);
        }
    }

    // ── No changes when we can't safely act ──────────────────────────────────────

    public class NoOpTests : TeamsPaginationHookTests
    {
        [Fact]
        public async Task MakesNoChange_WhenSchemaIsUnknown()
        {
            var context = CreateContext("ListChats", arguments: null, inputSchema: null);
            var original = context.Request;

            await _hook.OnPreInvokeAsync(context);

            // Unknown schema -> never inject (this is what prevents the 'Unknown argument' break).
            context.Request.Should().BeSameAs(original);
            context.Request.Arguments.Should().BeNull();
        }

        [Fact]
        public async Task MakesNoChange_WhenToolDeclaresNeitherKnob()
        {
            var context = CreateContext("ListChats", arguments: null, inputSchema: NeitherSchema());
            var original = context.Request;

            await _hook.OnPreInvokeAsync(context);

            context.Request.Should().BeSameAs(original);
            context.Request.Arguments.Should().BeNull();
        }

        [Fact]
        public async Task MakesNoChange_ForNonPaginatedTool()
        {
            var context = CreateContext("GetChat", arguments: null, inputSchema: TopSchema());
            var original = context.Request;

            await _hook.OnPreInvokeAsync(context);

            context.Request.Should().BeSameAs(original);
        }
    }

    public class PriorityTests : TeamsPaginationHookTests
    {
        [Fact]
        public void Priority_Is100()
        {
            _hook.Priority.Should().Be(100);
        }
    }
}
