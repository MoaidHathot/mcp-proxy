using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Proxy;
using McpProxy.Sdk.Sdk;
using McpProxy.Tests.E2E.Fixtures;
using Microsoft.Extensions.Logging;

namespace McpProxy.Tests.E2E;

/// <summary>
/// Tests for per-server routing where each backend gets its own endpoint
/// with isolated tool, resource, and prompt lists.
/// </summary>
public class PerServerRoutingTests : ProxyTestBase
{
    [Fact]
    public async Task ListToolsAsync_ReturnsOnlyToolsFromOwnBackend()
    {
        // Arrange: Two servers with distinct tools
        var tool1 = CreateTool("calendar_create_event");
        var tool2 = CreateTool("calendar_list_events");
        var tool3 = CreateTool("mail_send_message");
        var tool4 = CreateTool("mail_read_inbox");

        var calendarClient = CreateMockClient("calendar", tools: [tool1, tool2]);
        var mailClient = CreateMockClient("mail", tools: [tool3, tool4]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");
        var mailProxy = CreateSingleServerProxy("mail");

        // Act
        var calendarTools = await calendarProxy.ListToolsAsync(TestContext.Current.CancellationToken);
        var mailTools = await mailProxy.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert: Each proxy only returns its own server's tools
        calendarTools.Tools.Should().HaveCount(2);
        calendarTools.Tools.Select(t => t.Name).Should().BeEquivalentTo(["calendar_create_event", "calendar_list_events"]);

        mailTools.Tools.Should().HaveCount(2);
        mailTools.Tools.Select(t => t.Name).Should().BeEquivalentTo(["mail_send_message", "mail_read_inbox"]);
    }

    [Fact]
    public async Task CallToolAsync_RoutesToOwnBackendOnly()
    {
        // Arrange
        var calendarTool = CreateTool("create_event");
        var mailTool = CreateTool("send_message");

        var calendarClient = CreateMockClient("calendar", tools: [calendarTool]);
        var mailClient = CreateMockClient("mail", tools: [mailTool]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");

        // Act: Call a tool through the calendar proxy
        var result = await calendarProxy.CallToolAsync(
            new ModelContextProtocol.Protocol.CallToolRequestParams { Name = "create_event" },
            TestContext.Current.CancellationToken);

        // Assert: Result comes from the calendar backend
        result.Content.Should().HaveCount(1);
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("calendar");
        textContent.Text.Should().Contain("create_event");
    }

    [Fact]
    public async Task ListToolsAsync_ToolFromOtherBackendNotVisible()
    {
        // Arrange: Two servers, query one
        var calendarTool = CreateTool("create_event");
        var mailTool = CreateTool("send_message");

        var calendarClient = CreateMockClient("calendar", tools: [calendarTool]);
        var mailClient = CreateMockClient("mail", tools: [mailTool]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");

        // Act
        var calendarTools = await calendarProxy.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert: Calendar proxy does NOT see mail tools
        calendarTools.Tools.Should().HaveCount(1);
        calendarTools.Tools.Select(t => t.Name).Should().NotContain("send_message");
    }

    [Fact]
    public async Task ListResourcesAsync_ReturnsOnlyResourcesFromOwnBackend()
    {
        // Arrange
        var calendarResource = CreateResource("calendar://events", "Events");
        var mailResource = CreateResource("mail://inbox", "Inbox");

        var calendarClient = CreateMockClient("calendar", resources: [calendarResource]);
        var mailClient = CreateMockClient("mail", resources: [mailResource]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");
        var mailProxy = CreateSingleServerProxy("mail");

        // Act
        var calendarResources = await calendarProxy.ListResourcesAsync(TestContext.Current.CancellationToken);
        var mailResources = await mailProxy.ListResourcesAsync(TestContext.Current.CancellationToken);

        // Assert
        calendarResources.Resources.Should().HaveCount(1);
        calendarResources.Resources[0].Name.Should().Be("Events");

        mailResources.Resources.Should().HaveCount(1);
        mailResources.Resources[0].Name.Should().Be("Inbox");
    }

    [Fact]
    public async Task ListPromptsAsync_ReturnsOnlyPromptsFromOwnBackend()
    {
        // Arrange
        var calendarPrompt = CreatePrompt("schedule_meeting");
        var mailPrompt = CreatePrompt("compose_email");

        var calendarClient = CreateMockClient("calendar", prompts: [calendarPrompt]);
        var mailClient = CreateMockClient("mail", prompts: [mailPrompt]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");
        var mailProxy = CreateSingleServerProxy("mail");

        // Act
        var calendarPrompts = await calendarProxy.ListPromptsAsync(TestContext.Current.CancellationToken);
        var mailPrompts = await mailProxy.ListPromptsAsync(TestContext.Current.CancellationToken);

        // Assert
        calendarPrompts.Prompts.Should().HaveCount(1);
        calendarPrompts.Prompts[0].Name.Should().Be("schedule_meeting");

        mailPrompts.Prompts.Should().HaveCount(1);
        mailPrompts.Prompts[0].Name.Should().Be("compose_email");
    }

    [Fact]
    public async Task ListToolsAsync_EmptyBackend_ReturnsEmptyList()
    {
        // Arrange: Server with no tools
        var client = CreateMockClient("empty-server", tools: []);
        RegisterClient("empty-server", client);

        var proxy = CreateSingleServerProxy("empty-server");

        // Act
        var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().BeEmpty();
    }

    /// <summary>
    /// Creates a <see cref="SingleServerProxy"/> bound to a specific server,
    /// simulating per-server routing mode.
    /// </summary>
    private SingleServerProxy CreateSingleServerProxy(
        string serverName,
        ServerConfiguration? config = null,
        IReadOnlyList<VirtualToolDefinition>? virtualTools = null)
    {
        var serverConfig = config ?? new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock"
        };

        var logger = Substitute.For<ILogger<SingleServerProxy>>();
        return new SingleServerProxy(logger, ClientManager, serverName, serverConfig, virtualTools: virtualTools);
    }

    [Fact]
    public async Task ListToolsAsync_Includes_PerServer_VirtualTools()
    {
        // Arrange: server with one backend tool + one virtual tool
        var backendTool = CreateTool("backend_tool");
        var virtualTool = new VirtualToolDefinition
        {
            Tool = CreateTool("virtual_tool"),
            Handler = (_, _) => ValueTask.FromResult(new ModelContextProtocol.Protocol.CallToolResult
            {
                Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "virtual result" }]
            })
        };

        var client = CreateMockClient("server1", tools: [backendTool]);
        RegisterClient("server1", client);

        var proxy = CreateSingleServerProxy("server1", virtualTools: [virtualTool]);

        // Act
        var result = await proxy.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert: both backend and virtual tools present
        result.Tools.Should().HaveCount(2);
        result.Tools.Select(t => t.Name).Should().Contain("backend_tool");
        result.Tools.Select(t => t.Name).Should().Contain("virtual_tool");
    }

    [Fact]
    public async Task CallToolAsync_Routes_VirtualTool_To_Handler()
    {
        // Arrange
        var handlerCalled = false;
        var virtualTool = new VirtualToolDefinition
        {
            Tool = CreateTool("my_virtual"),
            Handler = (request, _) =>
            {
                handlerCalled = true;
                return ValueTask.FromResult(new ModelContextProtocol.Protocol.CallToolResult
                {
                    Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = $"virtual:{request.Name}" }]
                });
            }
        };

        var client = CreateMockClient("server1", tools: [CreateTool("backend_tool")]);
        RegisterClient("server1", client);

        var proxy = CreateSingleServerProxy("server1", virtualTools: [virtualTool]);

        // Act
        var result = await proxy.CallToolAsync(
            new ModelContextProtocol.Protocol.CallToolRequestParams { Name = "my_virtual" },
            TestContext.Current.CancellationToken);

        // Assert
        handlerCalled.Should().BeTrue();
        var text = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        text!.Text.Should().Be("virtual:my_virtual");

        // Backend should NOT be called
        await client.DidNotReceive().CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CallToolAsync_Routes_Backend_Tool_When_Not_Virtual()
    {
        // Arrange
        var virtualTool = new VirtualToolDefinition
        {
            Tool = CreateTool("virtual_only"),
            Handler = (_, _) => ValueTask.FromResult(new ModelContextProtocol.Protocol.CallToolResult
            {
                Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = "virtual" }]
            })
        };

        var client = CreateMockClient("server1", tools: [CreateTool("backend_tool")]);
        RegisterClient("server1", client);

        var proxy = CreateSingleServerProxy("server1", virtualTools: [virtualTool]);

        // Act: call a backend tool, not the virtual one
        var result = await proxy.CallToolAsync(
            new ModelContextProtocol.Protocol.CallToolRequestParams { Name = "backend_tool" },
            TestContext.Current.CancellationToken);

        // Assert: should route to backend
        var text = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        text!.Text.Should().Contain("server1");
        text.Text.Should().Contain("backend_tool");
    }

    [Fact]
    public async Task SubscribeToResourceAsync_Routes_To_Own_Backend()
    {
        // Arrange
        var calendarClient = CreateMockClient("calendar", resources: [CreateResource("cal://events", "Events")]);
        var mailClient = CreateMockClient("mail", resources: [CreateResource("mail://inbox", "Inbox")]);

        RegisterClient("calendar", calendarClient);
        RegisterClient("mail", mailClient);

        var calendarProxy = CreateSingleServerProxy("calendar");

        // Act
        await calendarProxy.SubscribeToResourceAsync("cal://events", TestContext.Current.CancellationToken);

        // Assert: calendar client received the subscription
        await calendarClient.Received(1).SubscribeToResourceAsync("cal://events", Arg.Any<CancellationToken>());

        // Assert: mail client was NOT called
        await mailClient.DidNotReceive().SubscribeToResourceAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnsubscribeFromResourceAsync_Routes_To_Own_Backend()
    {
        // Arrange
        var client = CreateMockClient("server1", resources: [CreateResource("res://data", "Data")]);
        RegisterClient("server1", client);

        var proxy = CreateSingleServerProxy("server1");

        // Act
        await proxy.UnsubscribeFromResourceAsync("res://data", TestContext.Current.CancellationToken);

        // Assert
        await client.Received(1).UnsubscribeFromResourceAsync("res://data", Arg.Any<CancellationToken>());
    }
}
