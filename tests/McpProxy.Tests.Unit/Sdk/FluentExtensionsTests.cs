using McpProxy.Abstractions;
using McpProxy.SDK.Configuration;
using McpProxy.SDK.Sdk;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Sdk;

public sealed class FluentExtensionsTests
{
    [Fact]
    public void OnPreInvoke_AddsGlobalHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.OnPreInvoke(_ => ValueTask.CompletedTask);

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.GlobalPreInvokeHooks);
    }

    [Fact]
    public void OnPreInvoke_Sync_AddsGlobalHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.OnPreInvoke(_ => { /* sync action */ });

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.GlobalPreInvokeHooks);
    }

    [Fact]
    public void OnPostInvoke_AddsGlobalHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.OnPostInvoke((_, result) => ValueTask.FromResult(result));

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.GlobalPostInvokeHooks);
    }

    [Fact]
    public void OnPostInvoke_Sync_AddsGlobalHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.OnPostInvoke((_, result) => result);

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.GlobalPostInvokeHooks);
    }

    [Fact]
    public void InterceptTools_AddsInterceptor()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.InterceptTools(tools => tools.Where(t => t.Tool.Name.StartsWith("prefix")));

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ToolInterceptors);
    }

    [Fact]
    public void InterceptToolCalls_AddsInterceptor()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.InterceptToolCalls((_, _) => ValueTask.FromResult<CallToolResult?>(null));

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ToolCallInterceptors);
    }

    [Fact]
    public void AddTool_WithAsync_AddsVirtualTool()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddTool("my-tool", "My tool description", (req, ct) =>
            ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Hello!" }]
            }));

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.VirtualTools);
        Assert.Equal("my-tool", config.VirtualTools[0].Tool.Name);
        Assert.Equal("My tool description", config.VirtualTools[0].Tool.Description);
    }

    [Fact]
    public void AddTool_WithSync_AddsVirtualTool()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddTool("sync-tool", "Sync tool", req =>
            new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Sync result" }]
            });

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.VirtualTools);
        Assert.Equal("sync-tool", config.VirtualTools[0].Tool.Name);
    }

    [Fact]
    public void RemoveTools_AddsInterceptorThatFilters()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.RemoveTools((tool, _) => tool.Name == "remove-me");

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ToolInterceptors);

        // Verify interceptor behavior
        var tools = new List<ToolWithServer>
        {
            new() { Tool = new Tool { Name = "remove-me" }, OriginalName = "remove-me", ServerName = "s", Include = true },
            new() { Tool = new Tool { Name = "keep-me" }, OriginalName = "keep-me", ServerName = "s", Include = true }
        };

        var result = config.ToolInterceptors[0].InterceptTools(tools).ToList();

        Assert.False(result.First(t => t.Tool.Name == "remove-me").Include);
        Assert.True(result.First(t => t.Tool.Name == "keep-me").Include);
    }

    [Fact]
    public void RemoveToolsByPattern_RemovesMatchingTools()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.RemoveToolsByPattern("internal_*", "*_debug");

        var config = builder.BuildConfiguration();

        // Assert
        var tools = new List<ToolWithServer>
        {
            new() { Tool = new Tool { Name = "internal_tool" }, OriginalName = "internal_tool", ServerName = "s", Include = true },
            new() { Tool = new Tool { Name = "my_debug" }, OriginalName = "my_debug", ServerName = "s", Include = true },
            new() { Tool = new Tool { Name = "public_tool" }, OriginalName = "public_tool", ServerName = "s", Include = true }
        };

        var result = config.ToolInterceptors[0].InterceptTools(tools).ToList();

        Assert.False(result.First(t => t.Tool.Name == "internal_tool").Include);
        Assert.False(result.First(t => t.Tool.Name == "my_debug").Include);
        Assert.True(result.First(t => t.Tool.Name == "public_tool").Include);
    }

    [Fact]
    public void ModifyTools_ModifiesMatchingTools()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.ModifyTools(
            (tool, _) => tool.Name.StartsWith("old_"),
            tool => new Tool
            {
                Name = tool.Name.Replace("old_", "new_"),
                Description = tool.Description
            });

        var config = builder.BuildConfiguration();

        // Assert
        var tools = new List<ToolWithServer>
        {
            new() { Tool = new Tool { Name = "old_tool" }, OriginalName = "old_tool", ServerName = "s" },
            new() { Tool = new Tool { Name = "other_tool" }, OriginalName = "other_tool", ServerName = "s" }
        };

        var result = config.ToolInterceptors[0].InterceptTools(tools).ToList();

        Assert.Equal("new_tool", result.First(t => t.OriginalName == "old_tool").Tool.Name);
        Assert.Equal("other_tool", result.First(t => t.OriginalName == "other_tool").Tool.Name);
    }

    [Fact]
    public void RenameTool_RenamesTool()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.RenameTool("old_name", "new_name");

        var config = builder.BuildConfiguration();

        // Assert
        var tools = new List<ToolWithServer>
        {
            new() { Tool = new Tool { Name = "old_name" }, OriginalName = "old_name", ServerName = "s" },
            new() { Tool = new Tool { Name = "other" }, OriginalName = "other", ServerName = "s" }
        };

        var result = config.ToolInterceptors[0].InterceptTools(tools).ToList();

        Assert.Equal("new_name", result.First(t => t.OriginalName == "old_name").Tool.Name);
        Assert.Equal("other", result.First(t => t.OriginalName == "other").Tool.Name);
    }

    [Fact]
    public void ServerBuilder_OnPreInvoke_AddsHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddStdioServer("server", "cmd")
            .OnPreInvoke(_ => ValueTask.CompletedTask)
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ServerStates["server"].PreInvokeHooks);
    }

    [Fact]
    public void ServerBuilder_OnPostInvoke_AddsHook()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddStdioServer("server", "cmd")
            .OnPostInvoke((_, result) => ValueTask.FromResult(result))
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Single(config.ServerStates["server"].PostInvokeHooks);
    }

    [Fact]
    public void ServerBuilder_FilterTools_AddsCustomFilter()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddStdioServer("server", "cmd")
            .FilterTools((tool, _) => tool.Name.StartsWith("allowed"))
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        Assert.NotNull(config.ServerStates["server"].CustomFilter);

        var allowedTool = new Tool { Name = "allowed_tool" };
        var blockedTool = new Tool { Name = "blocked_tool" };

        Assert.True(config.ServerStates["server"].CustomFilter!.ShouldInclude(allowedTool, "server"));
        Assert.False(config.ServerStates["server"].CustomFilter!.ShouldInclude(blockedTool, "server"));
    }

    [Fact]
    public void ServerBuilder_TransformTools_AddsCustomTransformer()
    {
        // Arrange
        var builder = McpProxyBuilder.Create();

        // Act
        builder.AddStdioServer("server", "cmd")
            .TransformTools((tool, serverName) => new Tool
            {
                Name = $"{serverName}:{tool.Name}",
                Description = tool.Description
            })
            .Build();

        var config = builder.BuildConfiguration();

        // Assert
        Assert.NotNull(config.ServerStates["server"].CustomTransformer);

        var tool = new Tool { Name = "original", Description = "desc" };
        var transformed = config.ServerStates["server"].CustomTransformer!.Transform(tool, "server");

        Assert.Equal("server:original", transformed.Name);
    }

    [Fact]
    public void ChainedConfiguration_BuildsCorrectly()
    {
        // Arrange & Act
        var builder = McpProxyBuilder.Create();

        builder
            .WithServerInfo("Test Proxy", "1.0.0", "Test instructions")
            .WithToolCaching(true, 120)
            .OnPreInvoke(ctx => { ctx.Items["timestamp"] = DateTime.UtcNow; return ValueTask.CompletedTask; })
            .OnPostInvoke((ctx, result) => ValueTask.FromResult(result))
            .AddTool("echo", "Echoes input", req => new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Echo!" }]
            })
            .AddStdioServer("backend", "node", "server.js")
                .WithTitle("Backend Server")
                .WithToolPrefix("be")
                .DenyTools("internal_*")
                .OnPreInvoke(ctx => { ctx.Items["server_hook"] = true; return ValueTask.CompletedTask; })
                .Build()
            .AddHttpServer("api", "http://localhost:3000")
                .WithHeaders(new Dictionary<string, string> { ["Authorization"] = "Bearer token" })
                .AllowTools("public_*")
                .Build()
            .RemoveToolsByPattern("deprecated_*");

        var config = builder.BuildConfiguration();

        // Assert
        Assert.Equal("Test Proxy", config.Configuration.Proxy.ServerInfo.Name);
        Assert.Equal("1.0.0", config.Configuration.Proxy.ServerInfo.Version);
        Assert.True(config.Configuration.Proxy.Caching.Tools.Enabled);
        Assert.Equal(120, config.Configuration.Proxy.Caching.Tools.TtlSeconds);

        Assert.Single(config.GlobalPreInvokeHooks);
        Assert.Single(config.GlobalPostInvokeHooks);
        Assert.Single(config.VirtualTools);
        Assert.Single(config.ToolInterceptors); // RemoveToolsByPattern

        Assert.Equal(2, config.Configuration.Mcp.Count);
        Assert.Equal("be", config.Configuration.Mcp["backend"].Tools.Prefix);
        Assert.Equal(FilterMode.DenyList, config.Configuration.Mcp["backend"].Tools.Filter.Mode);
        Assert.Single(config.ServerStates["backend"].PreInvokeHooks);

        Assert.Equal(FilterMode.AllowList, config.Configuration.Mcp["api"].Tools.Filter.Mode);
    }
}
