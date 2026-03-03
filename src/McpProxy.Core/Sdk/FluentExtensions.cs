using McpProxy.Abstractions;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Sdk;

/// <summary>
/// Fluent extension methods for configuring MCP Proxy using delegates.
/// </summary>
public static class McpProxyBuilderFluentExtensions
{
    /// <summary>
    /// Adds a pre-invoke hook using a delegate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder OnPreInvoke(
        this IMcpProxyBuilder builder,
        Func<HookContext<CallToolRequestParams>, ValueTask> handler,
        int priority = 0)
    {
        return builder.WithGlobalPreInvokeHook(new DelegatePreInvokeHook(handler, priority));
    }

    /// <summary>
    /// Adds a pre-invoke hook using a synchronous delegate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder OnPreInvoke(
        this IMcpProxyBuilder builder,
        Action<HookContext<CallToolRequestParams>> handler,
        int priority = 0)
    {
        return builder.WithGlobalPreInvokeHook(DelegatePreInvokeHook.FromAction(handler, priority));
    }

    /// <summary>
    /// Adds a post-invoke hook using a delegate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder OnPostInvoke(
        this IMcpProxyBuilder builder,
        Func<HookContext<CallToolRequestParams>, CallToolResult, ValueTask<CallToolResult>> handler,
        int priority = 0)
    {
        return builder.WithGlobalPostInvokeHook(new DelegatePostInvokeHook(handler, priority));
    }

    /// <summary>
    /// Adds a post-invoke hook using a synchronous delegate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder OnPostInvoke(
        this IMcpProxyBuilder builder,
        Func<HookContext<CallToolRequestParams>, CallToolResult, CallToolResult> handler,
        int priority = 0)
    {
        return builder.WithGlobalPostInvokeHook(DelegatePostInvokeHook.FromFunc(handler, priority));
    }

    /// <summary>
    /// Adds a tool interceptor using a delegate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="handler">The interceptor handler.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder InterceptTools(
        this IMcpProxyBuilder builder,
        Func<IEnumerable<ToolWithServer>, IEnumerable<ToolWithServer>> handler)
    {
        return builder.WithToolInterceptor(new DelegateToolInterceptor(handler));
    }

    /// <summary>
    /// Adds a tool call interceptor using a delegate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="handler">The interceptor handler.</param>
    /// <param name="priority">The interceptor priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder InterceptToolCalls(
        this IMcpProxyBuilder builder,
        Func<ToolCallContext, CancellationToken, ValueTask<CallToolResult?>> handler,
        int priority = 0)
    {
        return builder.WithToolCallInterceptor(new DelegateToolCallInterceptor(handler, priority));
    }

    /// <summary>
    /// Adds a virtual tool with inline handler.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="handler">The tool handler.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder AddTool(
        this IMcpProxyBuilder builder,
        string name,
        string description,
        Func<CallToolRequestParams, CancellationToken, ValueTask<CallToolResult>> handler)
    {
        var tool = new Tool
        {
            Name = name,
            Description = description
        };

        return builder.AddVirtualTool(tool, handler);
    }

    /// <summary>
    /// Adds a virtual tool with inline synchronous handler.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="handler">The tool handler.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder AddTool(
        this IMcpProxyBuilder builder,
        string name,
        string description,
        Func<CallToolRequestParams, CallToolResult> handler)
    {
        var tool = new Tool
        {
            Name = name,
            Description = description
        };

        return builder.AddVirtualTool(tool, (request, _) => ValueTask.FromResult(handler(request)));
    }

    /// <summary>
    /// Removes tools matching a predicate from all servers.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="predicate">The predicate to match tools to remove.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder RemoveTools(
        this IMcpProxyBuilder builder,
        Func<Tool, string, bool> predicate)
    {
        return builder.InterceptTools(tools => tools.Select(t =>
        {
            if (predicate(t.Tool, t.ServerName))
            {
                t.Include = false;
            }
            return t;
        }));
    }

    /// <summary>
    /// Removes tools by name pattern.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="patterns">Tool name patterns to remove (supports * and ? wildcards).</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder RemoveToolsByPattern(
        this IMcpProxyBuilder builder,
        params string[] patterns)
    {
        return builder.InterceptTools(tools => tools.Select(t =>
        {
            foreach (var pattern in patterns)
            {
                if (MatchesWildcard(t.Tool.Name, pattern))
                {
                    t.Include = false;
                    break;
                }
            }
            return t;
        }));
    }

    /// <summary>
    /// Modifies tools matching a predicate.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="predicate">The predicate to match tools to modify.</param>
    /// <param name="modifier">The modification function.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder ModifyTools(
        this IMcpProxyBuilder builder,
        Func<Tool, string, bool> predicate,
        Func<Tool, Tool> modifier)
    {
        return builder.InterceptTools(tools => tools.Select(t =>
        {
            if (predicate(t.Tool, t.ServerName))
            {
                t.Tool = modifier(t.Tool);
            }
            return t;
        }));
    }

    /// <summary>
    /// Renames a tool.
    /// </summary>
    /// <param name="builder">The proxy builder.</param>
    /// <param name="oldName">The original tool name.</param>
    /// <param name="newName">The new tool name.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMcpProxyBuilder RenameTool(
        this IMcpProxyBuilder builder,
        string oldName,
        string newName)
    {
        return builder.InterceptTools(tools => tools.Select(t =>
        {
            if (string.Equals(t.Tool.Name, oldName, StringComparison.OrdinalIgnoreCase))
            {
                t.Tool = new Tool
                {
                    Name = newName,
                    Description = t.Tool.Description,
                    InputSchema = t.Tool.InputSchema,
                    Annotations = t.Tool.Annotations
                };
            }
            return t;
        }));
    }

    private static bool MatchesWildcard(string text, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            text, 
            regexPattern, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Fluent extension methods for server builder.
/// </summary>
public static class ServerBuilderFluentExtensions
{
    /// <summary>
    /// Adds a pre-invoke hook using a delegate.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder OnPreInvoke(
        this IServerBuilder builder,
        Func<HookContext<CallToolRequestParams>, ValueTask> handler,
        int priority = 0)
    {
        return builder.WithPreInvokeHook(new DelegatePreInvokeHook(handler, priority));
    }

    /// <summary>
    /// Adds a pre-invoke hook using a synchronous delegate.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder OnPreInvoke(
        this IServerBuilder builder,
        Action<HookContext<CallToolRequestParams>> handler,
        int priority = 0)
    {
        return builder.WithPreInvokeHook(DelegatePreInvokeHook.FromAction(handler, priority));
    }

    /// <summary>
    /// Adds a post-invoke hook using a delegate.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder OnPostInvoke(
        this IServerBuilder builder,
        Func<HookContext<CallToolRequestParams>, CallToolResult, ValueTask<CallToolResult>> handler,
        int priority = 0)
    {
        return builder.WithPostInvokeHook(new DelegatePostInvokeHook(handler, priority));
    }

    /// <summary>
    /// Adds a post-invoke hook using a synchronous delegate.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="handler">The hook handler.</param>
    /// <param name="priority">The hook priority.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder OnPostInvoke(
        this IServerBuilder builder,
        Func<HookContext<CallToolRequestParams>, CallToolResult, CallToolResult> handler,
        int priority = 0)
    {
        return builder.WithPostInvokeHook(DelegatePostInvokeHook.FromFunc(handler, priority));
    }

    /// <summary>
    /// Adds a custom tool filter using a delegate.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="filter">The filter predicate.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder FilterTools(
        this IServerBuilder builder,
        Func<Tool, string, bool> filter)
    {
        return builder.WithToolFilter(new DelegateToolFilter(filter));
    }

    /// <summary>
    /// Adds a custom tool transformer using a delegate.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="transformer">The transformer function.</param>
    /// <returns>The builder for chaining.</returns>
    public static IServerBuilder TransformTools(
        this IServerBuilder builder,
        Func<Tool, string, Tool> transformer)
    {
        return builder.WithToolTransformer(new DelegateToolTransformer(transformer));
    }
}
