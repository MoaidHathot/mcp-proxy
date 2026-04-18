using McpProxy.Sdk.Configuration;
using McpProxy.Tests.E2E.Fixtures;

namespace McpProxy.Tests.E2E;

/// <summary>
/// Tests for tool filtering (allowlist/denylist) functionality.
/// </summary>
public class FilteringTests : ProxyTestBase
{
    [Fact]
    public async Task ListToolsAsync_WithDenyListFilter_ExcludesMatchingTools()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool("allowed-tool"),
            CreateTool("blocked-tool"),
            CreateTool("another-allowed"),
        };

        var client = CreateMockClient("server1", tools: tools);

        var config = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Filter = new FilterConfiguration
                {
                    Mode = FilterMode.DenyList,
                    Patterns = ["blocked*"]
                }
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

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(2);
        result.Tools.Select(t => t.Name).Should().BeEquivalentTo(["allowed-tool", "another-allowed"]);
        result.Tools.Should().NotContain(t => t.Name == "blocked-tool");
    }

    [Fact]
    public async Task ListToolsAsync_WithAllowListFilter_IncludesOnlyMatchingTools()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool("safe-read"),
            CreateTool("safe-write"),
            CreateTool("dangerous-delete"),
        };

        var client = CreateMockClient("server1", tools: tools);

        var config = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Filter = new FilterConfiguration
                {
                    Mode = FilterMode.AllowList,
                    Patterns = ["safe-*"]
                }
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

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(2);
        result.Tools.Select(t => t.Name).Should().BeEquivalentTo(["safe-read", "safe-write"]);
    }

    [Fact]
    public async Task ListToolsAsync_WithPrefixing_AddsPrefixToToolNames()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool("list-files"),
            CreateTool("read-file"),
        };

        var client = CreateMockClient("server1", tools: tools);

        var config = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Prefix = "fs"
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

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(2);
        result.Tools.Select(t => t.Name).Should().BeEquivalentTo(["fs_list-files", "fs_read-file"]);
    }

    [Fact]
    public async Task ListToolsAsync_WithCustomPrefixSeparator_UsesCustomSeparator()
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
                Prefix = "srv",
                PrefixSeparator = "::"
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

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(1);
        result.Tools[0].Name.Should().Be("srv::my-tool");
    }

    [Fact]
    public async Task ListToolsAsync_WithFilterAndPrefix_FiltersBeforePrefixing()
    {
        // Arrange
        var tools = new[]
        {
            CreateTool("allowed"),
            CreateTool("blocked"),
        };

        var client = CreateMockClient("server1", tools: tools);

        var config = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Prefix = "srv",
                Filter = new FilterConfiguration
                {
                    Mode = FilterMode.DenyList,
                    Patterns = ["blocked"]
                }
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

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(1);
        result.Tools[0].Name.Should().Be("srv_allowed");
    }

    [Fact]
    public async Task ListToolsAsync_MultipleBackendsWithDifferentFilters_AppliesFiltersCorrectly()
    {
        // Arrange
        var tools1 = new[]
        {
            CreateTool("read"),
            CreateTool("write"),
        };
        var tools2 = new[]
        {
            CreateTool("query"),
            CreateTool("delete"),
        };

        var client1 = CreateMockClient("server1", tools: tools1);
        var client2 = CreateMockClient("server2", tools: tools2);

        var config1 = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Filter = new FilterConfiguration
                {
                    Mode = FilterMode.AllowList,
                    Patterns = ["read"] // Only allow "read"
                }
            }
        };

        var config2 = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock",
            Tools = new ToolsConfiguration
            {
                Filter = new FilterConfiguration
                {
                    Mode = FilterMode.DenyList,
                    Patterns = ["delete"] // Block "delete"
                }
            }
        };

        RegisterClient("server1", client1, config1);
        RegisterClient("server2", client2, config2);

        var proxyConfig = new ProxyConfiguration
        {
            Mcp = new Dictionary<string, ServerConfiguration>
            {
                ["server1"] = config1,
                ["server2"] = config2
            }
        };
        var proxy = CreateProxyServer(proxyConfig);

        // Act
        var result = await proxy.ListToolsCoreAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Tools.Should().HaveCount(2);
        result.Tools.Select(t => t.Name).Should().BeEquivalentTo(["read", "query"]);
    }
}
