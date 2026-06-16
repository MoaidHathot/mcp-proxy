using System.Text.Json;
using McpProxy.Abstractions;
using McpProxy.Sdk.Hooks.BuiltIn;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)

namespace McpProxy.Tests.Unit.Hooks.BuiltIn;

public class SchemaArgumentGuardHookTests
{
    private readonly ILogger<SchemaArgumentGuardHook> _logger = Substitute.For<ILogger<SchemaArgumentGuardHook>>();

    // Strict object schema (declares properties, no additionalProperties) — mirrors the
    // backend ListChats schema: memberUpns, topic, fetchAllPages (and NO `top`).
    private static JsonElement StrictSchema() => JsonDocument.Parse(
        """{"type":"object","properties":{"memberUpns":{"type":"array"},"topic":{"type":"string"},"fetchAllPages":{"type":"boolean"}}}""").RootElement;

    private static JsonElement SchemaWithAdditionalTrue() => JsonDocument.Parse(
        """{"type":"object","properties":{"memberUpns":{"type":"array"}},"additionalProperties":true}""").RootElement;

    private static JsonElement SchemaWithAdditionalObject() => JsonDocument.Parse(
        """{"type":"object","properties":{"memberUpns":{"type":"array"}},"additionalProperties":{"type":"string"}}""").RootElement;

    private static JsonElement SchemaWithoutProperties() => JsonDocument.Parse(
        """{"type":"object"}""").RootElement;

    private static HookContext<CallToolRequestParams> CreateContext(
        Dictionary<string, JsonElement>? arguments = null,
        JsonElement? inputSchema = null,
        string toolName = "ListChats")
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "teams-server",
            ToolName = toolName,
            Request = new CallToolRequestParams { Name = toolName, Arguments = arguments },
            CancellationToken = TestContext.Current.CancellationToken,
            ToolInputSchema = inputSchema
        };
    }

    [Fact]
    public void Priority_RunsLast()
    {
        new SchemaArgumentGuardHook(_logger).Priority.Should().Be(1000);
    }

    [Fact]
    public async Task StripsUndeclaredArgument_AgainstStrictSchema()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["memberUpns"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
            ["top"] = JsonSerializer.SerializeToElement(20)
        };
        var context = CreateContext(args, StrictSchema());

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().NotContainKey("top");
        context.Request.Arguments.Should().ContainKey("memberUpns");
    }

    [Fact]
    public async Task KeepsDeclaredArguments_AndPreservesValues()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["topic"] = JsonSerializer.SerializeToElement("Standup"),
            ["fetchAllPages"] = JsonSerializer.SerializeToElement(true)
        };
        var context = CreateContext(args, StrictSchema());

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().HaveCount(2);
        context.Request.Arguments!["topic"].GetString().Should().Be("Standup");
        context.Request.Arguments["fetchAllPages"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task StripsStrayForceRefreshFalse()
    {
        // The cache interceptor only strips forceRefresh when it is `true`; a leftover
        // `forceRefresh: false` would otherwise reach (and be rejected by) the backend.
        var args = new Dictionary<string, JsonElement>
        {
            ["forceRefresh"] = JsonSerializer.SerializeToElement(false)
        };
        var context = CreateContext(args, StrictSchema());

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().NotContainKey("forceRefresh");
    }

    [Fact]
    public async Task DoesNotStrip_WhenAdditionalPropertiesTrue()
    {
        var args = new Dictionary<string, JsonElement> { ["anything"] = JsonSerializer.SerializeToElement(1) };
        var original = args;
        var context = CreateContext(args, SchemaWithAdditionalTrue());

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().BeSameAs(original);
        context.Request.Arguments.Should().ContainKey("anything");
    }

    [Fact]
    public async Task DoesNotStrip_WhenAdditionalPropertiesIsSchemaObject()
    {
        var args = new Dictionary<string, JsonElement> { ["anything"] = JsonSerializer.SerializeToElement("x") };
        var context = CreateContext(args, SchemaWithAdditionalObject());

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().ContainKey("anything");
    }

    [Fact]
    public async Task DoesNotStrip_WhenSchemaHasNoProperties()
    {
        var args = new Dictionary<string, JsonElement> { ["anything"] = JsonSerializer.SerializeToElement("x") };
        var context = CreateContext(args, SchemaWithoutProperties());

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().ContainKey("anything");
    }

    [Fact]
    public async Task DoesNotStrip_WhenSchemaIsUnknown()
    {
        var args = new Dictionary<string, JsonElement> { ["top"] = JsonSerializer.SerializeToElement(20) };
        var context = CreateContext(args, inputSchema: null);

        await new SchemaArgumentGuardHook(_logger).OnPreInvokeAsync(context);

        context.Request.Arguments.Should().ContainKey("top");
    }

    [Fact]
    public async Task LogsButKeeps_WhenStrippingDisabled()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["memberUpns"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
            ["top"] = JsonSerializer.SerializeToElement(20)
        };
        var context = CreateContext(args, StrictSchema());
        var hook = new SchemaArgumentGuardHook(_logger, new SchemaArgumentGuardConfiguration { StripUnknownArguments = false });

        await hook.OnPreInvokeAsync(context);

        context.Request.Arguments.Should().ContainKey("top");
    }
}
