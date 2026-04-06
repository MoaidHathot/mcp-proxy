using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Sdk;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Sdk;

public sealed class McpProxyBuilderTests
{
    [Fact]
    public void Create_ReturnsNewBuilder()
    {
        // Act
        var builder = McpProxyBuilder.Create();

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void AddStdioServer_ConfiguresServerCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddStdioServer("test-server", "node", "index.js", "--port", "3000");
        var config = builder.BuildConfiguration();

        // Assert
        Assert.True(config.Configuration.Mcp.ContainsKey("test-server"));
        var serverConfig = config.Configuration.Mcp["test-server"];
        Assert.Equal(ServerTransportType.Stdio, serverConfig.Type);
        Assert.Equal("node", serverConfig.Command);
        Assert.NotNull(serverConfig.Arguments);
        Assert.Equal(["index.js", "--port", "3000"], serverConfig.Arguments);
    }

    [Fact]
    public void AddHttpServer_ConfiguresServerCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddHttpServer("http-server", "http://localhost:8080");
        var config = builder.BuildConfiguration();

        // Assert
        Assert.True(config.Configuration.Mcp.ContainsKey("http-server"));
        var serverConfig = config.Configuration.Mcp["http-server"];
        Assert.Equal(ServerTransportType.Http, serverConfig.Type);
        Assert.Equal("http://localhost:8080", serverConfig.Url);
    }

    [Fact]
    public void AddSseServer_ConfiguresServerCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddSseServer("sse-server", "http://localhost:8080/sse");
        var config = builder.BuildConfiguration();

        // Assert
        Assert.True(config.Configuration.Mcp.ContainsKey("sse-server"));
        var serverConfig = config.Configuration.Mcp["sse-server"];
        Assert.Equal(ServerTransportType.Sse, serverConfig.Type);
        Assert.Equal("http://localhost:8080/sse", serverConfig.Url);
    }

    [Fact]
    public void ServerBuilder_ConfiguresAllOptions()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var environment = new Dictionary<string, string> { ["KEY"] = "value" };

        // Act
        builder.AddStdioServer("server", "cmd")
            .WithTitle("Test Server")
            .WithDescription("A test server")
            .WithEnvironment(environment)
            .WithRoute("/custom/route")
            .WithToolPrefix("prefix", "-")
            .WithResourcePrefix("res", ":")
            .WithPromptPrefix("pmt", "_")
            .AllowTools("tool1", "tool2*")
            .Enabled(true)
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        var serverConfig = config.Configuration.Mcp["server"];
        Assert.Equal("Test Server", serverConfig.Title);
        Assert.Equal("A test server", serverConfig.Description);
        Assert.Equal("value", serverConfig.Environment?["KEY"]);
        Assert.Equal("/custom/route", serverConfig.Route);
        Assert.Equal("prefix", serverConfig.Tools.Prefix);
        Assert.Equal("-", serverConfig.Tools.PrefixSeparator);
        Assert.Equal("res", serverConfig.Resources.Prefix);
        Assert.Equal(":", serverConfig.Resources.PrefixSeparator);
        Assert.Equal("pmt", serverConfig.Prompts.Prefix);
        Assert.Equal("_", serverConfig.Prompts.PrefixSeparator);
        Assert.Equal(FilterMode.AllowList, serverConfig.Tools.Filter.Mode);
        Assert.NotNull(serverConfig.Tools.Filter.Patterns);
        Assert.Equal(["tool1", "tool2*"], serverConfig.Tools.Filter.Patterns);
        Assert.True(serverConfig.Enabled);
    }

    [Fact]
    public void ServerBuilder_DenyTools_ConfiguresFilterCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddStdioServer("server", "cmd")
            .DenyTools("dangerous*", "internal_*")
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        var serverConfig = config.Configuration.Mcp["server"];
        Assert.Equal(FilterMode.DenyList, serverConfig.Tools.Filter.Mode);
        Assert.NotNull(serverConfig.Tools.Filter.Patterns);
        Assert.Equal(["dangerous*", "internal_*"], serverConfig.Tools.Filter.Patterns);
    }

    [Fact]
    public void WithServerInfo_ConfiguresProxyInfo()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.WithServerInfo("MyProxy", "2.0.0", "Custom instructions");
        var config = builder.BuildConfiguration();

        // Assert
        Assert.Equal("MyProxy", config.Configuration.Proxy.ServerInfo.Name);
        Assert.Equal("2.0.0", config.Configuration.Proxy.ServerInfo.Version);
        Assert.Equal("Custom instructions", config.Configuration.Proxy.ServerInfo.Instructions);
    }

    [Fact]
    public void WithToolCaching_ConfiguresCache()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.WithToolCaching(true, 600);
        var config = builder.BuildConfiguration();

        // Assert
        Assert.True(config.Configuration.Proxy.Caching.Tools.Enabled);
        Assert.Equal(600, config.Configuration.Proxy.Caching.Tools.TtlSeconds);
    }

    [Fact]
    public void AddVirtualTool_RegistersTool()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var tool = new Tool
        {
            Name = "virtual-tool",
            Description = "A virtual tool"
        };

        // Act
        builder.AddVirtualTool(tool, (req, ct) => 
            ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "result" }]
            }));
        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.VirtualTools);
        Assert.Equal("virtual-tool", config.VirtualTools[0].Tool.Name);
    }

    [Fact]
    public void WithGlobalPreInvokeHook_RegistersHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var hook = new DelegatePreInvokeHook(_ => ValueTask.CompletedTask);

        // Act
        builder.WithGlobalPreInvokeHook(hook);
        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.GlobalPreInvokeHooks);
    }

    [Fact]
    public void WithGlobalPostInvokeHook_RegistersHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var hook = new DelegatePostInvokeHook((_, result) => ValueTask.FromResult(result));

        // Act
        builder.WithGlobalPostInvokeHook(hook);
        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.GlobalPostInvokeHooks);
    }

    [Fact]
    public void WithToolInterceptor_RegistersInterceptor()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var interceptor = new DelegateToolInterceptor(tools => tools);

        // Act
        builder.WithToolInterceptor(interceptor);
        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ToolInterceptors);
    }

    [Fact]
    public void WithToolCallInterceptor_RegistersInterceptor()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var interceptor = new DelegateToolCallInterceptor((_, _) => 
            ValueTask.FromResult<CallToolResult?>(null));

        // Act
        builder.WithToolCallInterceptor(interceptor);
        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ToolCallInterceptors);
    }

    [Fact]
    public void ServerBuilder_WithHooks_RegistersCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var preHook = new DelegatePreInvokeHook(_ => ValueTask.CompletedTask);
        var postHook = new DelegatePostInvokeHook((_, result) => ValueTask.FromResult(result));

        // Act
        builder.AddStdioServer("server", "cmd")
            .WithPreInvokeHook(preHook)
            .WithPostInvokeHook(postHook)
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        var serverState = config.ServerStates["server"];
        Assert.Single(serverState.PreInvokeHooks);
        Assert.Single(serverState.PostInvokeHooks);
    }

    [Fact]
    public void ServerBuilder_WithCustomFilter_RegistersCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var filter = new DelegateToolFilter((tool, _) => tool.Name.StartsWith("allowed"));

        // Act
        builder.AddStdioServer("server", "cmd")
            .WithToolFilter(filter)
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        var serverState = config.ServerStates["server"];
        Assert.NotNull(serverState.CustomFilter);
    }

    [Fact]
    public void ServerBuilder_WithCustomTransformer_RegistersCorrectly()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();
        var transformer = new DelegateToolTransformer((tool, _) => tool);

        // Act
        builder.AddStdioServer("server", "cmd")
            .WithToolTransformer(transformer)
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        var serverState = config.ServerStates["server"];
        Assert.NotNull(serverState.CustomTransformer);
    }
}
