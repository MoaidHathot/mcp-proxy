using System.Text.Json;
using McpProxy.Sdk.Configuration;
using McpProxy.Tests.E2E.Fixtures;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.E2E;

/// <summary>
/// Tests for tool call routing to the correct backend server.
/// </summary>
public class ToolRoutingTests : ProxyTestBase
{
    [Fact]
    public async Task CallToolAsync_RoutesToCorrectBackend()
    {
        // Arrange
        var tool1 = CreateTool("tool1");
        var tool2 = CreateTool("tool2");

        var client1 = CreateMockClient("server1", tools: [tool1]);
        var client2 = CreateMockClient("server2", tools: [tool2]);

        RegisterClient("server1", client1);
        RegisterClient("server2", client2);

        var proxy = CreateProxyServer();

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "tool2" },
            TestContext.Current.CancellationToken);

        // Assert
        result.Content.Should().HaveCount(1);
        var textContent = result.Content[0] as TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("server2");
        textContent.Text.Should().Contain("tool2");
    }

    [Fact]
    public async Task CallToolAsync_UnknownTool_ReturnsError()
    {
        // Arrange
        var tool = CreateTool("known-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // First list tools to populate the lookup
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "unknown-tool" },
            TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content.Should().HaveCount(1);
        var textContent = result.Content[0] as TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("not found");
    }

    [Fact]
    public async Task CallToolAsync_WithPrefixedTool_StripsPrefixBeforeRouting()
    {
        // Arrange
        var tool = CreateTool("my-tool");
        var client = CreateMockClient("server1", tools: [tool]);

        var config = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Prefix = "srv1"
            }
        };
        RegisterClient("server1", client, config);

        var proxyConfig = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["server1"] = config
            }
        };
        var proxy = CreateProxyServer(proxyConfig);

        // List tools to populate lookup (tools will be prefixed)
        var listResult = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Verify the tool is prefixed in the listing
        listResult.Tools.Should().Contain(t => t.Name == "srv1_my-tool");

        // Act - Call with prefixed name
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams { Name = "srv1_my-tool" },
            TestContext.Current.CancellationToken);

        // Assert - Should succeed and contain server name
        result.IsError.Should().NotBeTrue();
        result.Content.Should().HaveCount(1);
        var textContent = result.Content[0] as TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("server1");
    }

    [Fact]
    public async Task CallToolAsync_WithArguments_PassesArgumentsToBackend()
    {
        // Arrange
        var tool = CreateTool("echo-tool");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // First list tools
        await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        var arguments = new Dictionary<string, JsonElement>
        {
            ["message"] = JsonSerializer.SerializeToElement("hello"),
            ["count"] = JsonSerializer.SerializeToElement(42)
        };

        // Act
        var result = await proxy.CallToolCoreAsync(
            new CallToolRequestParams
            {
                Name = "echo-tool",
                Arguments = arguments
            },
            TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().NotBeTrue();
    }
}
