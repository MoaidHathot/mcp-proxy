using McpProxy.Tests.E2E.Fixtures;

namespace McpProxy.Tests.E2E;

/// <summary>
/// Tests for tool aggregation across multiple backend servers.
/// </summary>
public class ToolAggregationTests : ProxyTestBase
{
    [Fact]
    public async Task ListToolsAsync_SingleBackend_ReturnsAllTools()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool("tool1", "Tool 1 description"),
            CreateTool("tool2", "Tool 2 description"),
        };

        var client = CreateMockClient("server1", tools: tools);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(2);
        result.Tools.Select(t => t.Name).Should().BeEquivalentTo(["tool1", "tool2"]);
    }

    [Fact]
    public async Task ListToolsAsync_MultipleBackends_AggregatesTools()
    {
        // Arrange
        var tools1 = new[]
        {
            CreateTool("toolA", "Tool A from server1"),
        };
        var tools2 = new[]
        {
            CreateTool("toolB", "Tool B from server2"),
            CreateTool("toolC", "Tool C from server2"),
        };

        var client1 = CreateMockClient("server1", tools: tools1);
        var client2 = CreateMockClient("server2", tools: tools2);

        RegisterClient("server1", client1);
        RegisterClient("server2", client2);

        var proxy = CreateProxyServer();

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(3);
        result.Tools.Select(t => t.Name).Should().BeEquivalentTo(["toolA", "toolB", "toolC"]);
    }

    [Fact]
    public async Task ListToolsAsync_NoBackends_ReturnsEmptyList()
    {
        // Arrange
        var proxy = CreateProxyServer();

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ListToolsAsync_BackendWithNoTools_ReturnsEmptyList()
    {
        // Arrange
        var client = CreateMockClient("server1", tools: []);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ListToolsAsync_PreservesToolMetadata()
    {
        // Arrange
        var tool = CreateTool("mytool", "My tool description", "My Tool Title");
        var client = CreateMockClient("server1", tools: [tool]);
        RegisterClient("server1", client);

        var proxy = CreateProxyServer();

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(1);
        var resultTool = result.Tools[0];
        resultTool.Name.Should().Be("mytool");
        resultTool.Description.Should().Be("My tool description");
        resultTool.Title.Should().Be("My Tool Title");
    }
}
