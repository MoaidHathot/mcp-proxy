using McpProxy.Abstractions;
using McpProxy.SDK.Sdk;
using ModelContextProtocol.Protocol;

namespace McpProxy.Tests.Unit.Sdk;

public sealed class DelegateHooksTests
{
    [Fact]
    public async Task DelegatePreInvokeHook_ExecutesHandler()
    {
        // Arrange
        var executed = false;
        var hook = new DelegatePreInvokeHook(_ =>
        {
            executed = true;
            return ValueTask.CompletedTask;
        });
        var context = CreateHookContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task DelegatePreInvokeHook_FromAction_ExecutesHandler()
    {
        // Arrange
        var executed = false;
        var hook = DelegatePreInvokeHook.FromAction(_ => executed = true);
        var context = CreateHookContext();

        // Act
        await hook.OnPreInvokeAsync(context);

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public void DelegatePreInvokeHook_ReportsPriority()
    {
        // Arrange & Act
        var hook = new DelegatePreInvokeHook(_ => ValueTask.CompletedTask, priority: 42);

        // Assert
        Assert.Equal(42, hook.Priority);
    }

    [Fact]
    public async Task DelegatePostInvokeHook_ExecutesHandlerAndReturnsResult()
    {
        // Arrange
        var hook = new DelegatePostInvokeHook((_, result) =>
        {
            var modified = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "modified" }],
                IsError = result.IsError
            };
            return ValueTask.FromResult(modified);
        });
        var context = CreateHookContext();
        var originalResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "original" }]
        };

        // Act
        var result = await hook.OnPostInvokeAsync(context, originalResult);

        // Assert
        Assert.Equal("modified", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task DelegatePostInvokeHook_FromFunc_ExecutesHandler()
    {
        // Arrange
        var hook = DelegatePostInvokeHook.FromFunc((_, result) => result);
        var context = CreateHookContext();
        var originalResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "test" }]
        };

        // Act
        var result = await hook.OnPostInvokeAsync(context, originalResult);

        // Assert
        Assert.Same(originalResult, result);
    }

    [Fact]
    public async Task DelegateToolHook_ExecutesBothHandlers()
    {
        // Arrange
        var preExecuted = false;
        var postExecuted = false;
        var hook = new DelegateToolHook(
            preInvokeHandler: _ =>
            {
                preExecuted = true;
                return ValueTask.CompletedTask;
            },
            postInvokeHandler: (_, result) =>
            {
                postExecuted = true;
                return ValueTask.FromResult(result);
            }
        );
        var context = CreateHookContext();
        var result = new CallToolResult();

        // Act
        await hook.OnPreInvokeAsync(context);
        await hook.OnPostInvokeAsync(context, result);

        // Assert
        Assert.True(preExecuted);
        Assert.True(postExecuted);
    }

    [Fact]
    public void DelegateToolInterceptor_InterceptsTools()
    {
        // Arrange
        var interceptor = new DelegateToolInterceptor(tools =>
            tools.Where(t => t.Tool.Name.StartsWith("allowed")));

        var tools = new List<ToolWithServer>
        {
            new() { Tool = new Tool { Name = "allowed_tool" }, OriginalName = "allowed_tool", ServerName = "server" },
            new() { Tool = new Tool { Name = "blocked_tool" }, OriginalName = "blocked_tool", ServerName = "server" }
        };

        // Act
        var result = interceptor.InterceptTools(tools).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("allowed_tool", result[0].Tool.Name);
    }

    [Fact]
    public async Task DelegateToolCallInterceptor_ReturnsNullToContinue()
    {
        // Arrange
        var interceptor = new DelegateToolCallInterceptor((_, _) => 
            ValueTask.FromResult<CallToolResult?>(null));
        var context = CreateToolCallContext();

        // Act
        var result = await interceptor.InterceptAsync(context, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DelegateToolCallInterceptor_ReturnsResultToShortCircuit()
    {
        // Arrange
        var expected = new CallToolResult { Content = [new TextContentBlock { Text = "intercepted" }] };
        var interceptor = new DelegateToolCallInterceptor((_, _) => 
            ValueTask.FromResult<CallToolResult?>(expected));
        var context = CreateToolCallContext();

        // Act
        var result = await interceptor.InterceptAsync(context, CancellationToken.None);

        // Assert
        Assert.Same(expected, result);
    }

    [Fact]
    public void DelegateToolFilter_FiltersCorrectly()
    {
        // Arrange
        var filter = new DelegateToolFilter((tool, _) => tool.Name.Contains("include"));
        var includeTool = new Tool { Name = "include_this" };
        var excludeTool = new Tool { Name = "exclude_this" };

        // Act & Assert
        Assert.True(filter.ShouldInclude(includeTool, "server"));
        Assert.False(filter.ShouldInclude(excludeTool, "server"));
    }

    [Fact]
    public void DelegateToolTransformer_TransformsCorrectly()
    {
        // Arrange
        var transformer = new DelegateToolTransformer((tool, serverName) =>
            new Tool
            {
                Name = $"{serverName}_{tool.Name}",
                Description = tool.Description
            });
        var tool = new Tool { Name = "original", Description = "desc" };

        // Act
        var result = transformer.Transform(tool, "server");

        // Assert
        Assert.Equal("server_original", result.Name);
        Assert.Equal("desc", result.Description);
    }

    private static HookContext<CallToolRequestParams> CreateHookContext()
    {
        return new HookContext<CallToolRequestParams>
        {
            ServerName = "test-server",
            ToolName = "test-tool",
            Request = new CallToolRequestParams { Name = "test-tool" }
        };
    }

    private static ToolCallContext CreateToolCallContext()
    {
        return new ToolCallContext
        {
            ToolName = "test-tool",
            OriginalToolName = "test-tool",
            ServerName = "test-server",
            Request = new CallToolRequestParams { Name = "test-tool" }
        };
    }
}
