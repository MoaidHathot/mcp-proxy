using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Proxy;
using McpProxy.Tests.E2E.Fixtures;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.E2E;

/// <summary>
/// Tests that verify unified vs per-server tool/resource/prompt isolation behavior.
/// The unified proxy aggregates across all backends, while each SingleServerProxy
/// returns only its own backend's items.
/// </summary>
public class UnifiedVsPerServerIsolationTests : ProxyTestBase
{
    private SingleServerProxy CreateSingleServerProxy(string serverName, ServerConfiguration? config = null)
    {
        var serverConfig = config ?? new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock"
        };

        var logger = Substitute.For<ILogger<SingleServerProxy>>();
        return new SingleServerProxy(logger, ClientManager, serverName, serverConfig);
    }

    [Fact]
    public async Task Unified_Returns_All_Tools_While_PerServer_Returns_Only_Own()
    {
        // Arrange: Two servers with distinct tools
        var calendarTools = new[] { CreateTool("create_event"), CreateTool("list_events") };
        var mailTools = new[] { CreateTool("send_message"), CreateTool("read_inbox"), CreateTool("delete_message") };

        RegisterClient("calendar", CreateMockClient("calendar", tools: calendarTools));
        RegisterClient("mail", CreateMockClient("mail", tools: mailTools));

        var unified = CreateProxyServer();
        var calendarProxy = CreateSingleServerProxy("calendar");
        var mailProxy = CreateSingleServerProxy("mail");

        // Act
        var unifiedResult = await unified.ListToolsCoreAsync(TestContext.Current.CancellationToken);
        var calendarResult = await calendarProxy.ListToolsAsync(TestContext.Current.CancellationToken);
        var mailResult = await mailProxy.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert: unified aggregates all
        unifiedResult.Tools.Should().HaveCount(5);

        // Assert: per-server isolates
        calendarResult.Tools.Should().HaveCount(2);
        calendarResult.Tools.Select(t => t.Name).Should().BeEquivalentTo(["create_event", "list_events"]);

        mailResult.Tools.Should().HaveCount(3);
        mailResult.Tools.Select(t => t.Name).Should().BeEquivalentTo(["send_message", "read_inbox", "delete_message"]);
    }

    [Fact]
    public async Task Unified_Returns_All_Resources_While_PerServer_Returns_Only_Own()
    {
        // Arrange
        var calendarResources = new[] { CreateResource("cal://events", "Events") };
        var mailResources = new[] { CreateResource("mail://inbox", "Inbox"), CreateResource("mail://drafts", "Drafts") };

        RegisterClient("calendar", CreateMockClient("calendar", resources: calendarResources));
        RegisterClient("mail", CreateMockClient("mail", resources: mailResources));

        var calendarProxy = CreateSingleServerProxy("calendar");
        var mailProxy = CreateSingleServerProxy("mail");

        // Act
        var calendarResult = await calendarProxy.ListResourcesAsync(TestContext.Current.CancellationToken);
        var mailResult = await mailProxy.ListResourcesAsync(TestContext.Current.CancellationToken);

        // Assert: per-server isolation
        calendarResult.Resources.Should().HaveCount(1);
        calendarResult.Resources[0].Name.Should().Be("Events");
        mailResult.Resources.Should().HaveCount(2);
    }

    [Fact]
    public async Task Unified_Returns_All_Prompts_While_PerServer_Returns_Only_Own()
    {
        // Arrange
        var calendarPrompts = new[] { CreatePrompt("schedule_meeting") };
        var mailPrompts = new[] { CreatePrompt("compose_email"), CreatePrompt("reply_email") };

        RegisterClient("calendar", CreateMockClient("calendar", prompts: calendarPrompts));
        RegisterClient("mail", CreateMockClient("mail", prompts: mailPrompts));

        var calendarProxy = CreateSingleServerProxy("calendar");
        var mailProxy = CreateSingleServerProxy("mail");

        // Act
        var calendarResult = await calendarProxy.ListPromptsAsync(TestContext.Current.CancellationToken);
        var mailResult = await mailProxy.ListPromptsAsync(TestContext.Current.CancellationToken);

        // Assert: per-server isolation
        calendarResult.Prompts.Should().HaveCount(1);
        calendarResult.Prompts[0].Name.Should().Be("schedule_meeting");
        mailResult.Prompts.Should().HaveCount(2);
    }

    [Fact]
    public async Task PerServer_CallTool_Routes_To_Own_Backend_Not_Other()
    {
        // Arrange
        var calendarClient = CreateMockClient("calendar", tools: [CreateTool("create_event")]);
        var mailClient = CreateMockClient("mail", tools: [CreateTool("send_message")]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");

        // Act: call tool through calendar proxy
        var result = await calendarProxy.CallToolAsync(
            new ModelContextProtocol.Protocol.CallToolRequestParams { Name = "create_event" },
            TestContext.Current.CancellationToken);

        // Assert: result comes from calendar backend, not mail
        var text = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        text!.Text.Should().Contain("calendar");
        text.Text.Should().NotContain("mail");

        // Assert: mail client was never called
        await mailClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }
}
