using ModelContextProtocol.Protocol;

namespace McpProxy.Abstractions;

/// <summary>
/// Builder interface for configuring MCP Proxy programmatically.
/// Provides a fluent API for SDK-style consumption of the proxy.
/// </summary>
public interface IMcpProxyBuilder
{
    /// <summary>
    /// Adds a backend MCP server to the proxy using STDIO transport.
    /// </summary>
    /// <param name="name">Unique name for this server.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="arguments">Optional command arguments.</param>
    /// <returns>A server builder for further configuration.</returns>
    IServerBuilder AddStdioServer(string name, string command, params string[] arguments);

    /// <summary>
    /// Adds a backend MCP server to the proxy using HTTP transport.
    /// </summary>
    /// <param name="name">Unique name for this server.</param>
    /// <param name="url">The URL of the MCP server.</param>
    /// <returns>A server builder for further configuration.</returns>
    IServerBuilder AddHttpServer(string name, string url);

    /// <summary>
    /// Adds a backend MCP server to the proxy using SSE transport.
    /// </summary>
    /// <param name="name">Unique name for this server.</param>
    /// <param name="url">The URL of the MCP server.</param>
    /// <returns>A server builder for further configuration.</returns>
    IServerBuilder AddSseServer(string name, string url);

    /// <summary>
    /// Registers a global pre-invoke hook that applies to all servers.
    /// </summary>
    /// <param name="hook">The hook instance.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithGlobalPreInvokeHook(IPreInvokeHook hook);

    /// <summary>
    /// Registers a global post-invoke hook that applies to all servers.
    /// </summary>
    /// <param name="hook">The hook instance.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithGlobalPostInvokeHook(IPostInvokeHook hook);

    /// <summary>
    /// Registers a global tool hook that applies to all servers.
    /// </summary>
    /// <param name="hook">The hook instance.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithGlobalHook(IToolHook hook);

    /// <summary>
    /// Adds a virtual tool that is handled directly by the proxy without forwarding to any backend.
    /// </summary>
    /// <param name="tool">The tool definition.</param>
    /// <param name="handler">The handler function for the tool.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder AddVirtualTool(Tool tool, Func<CallToolRequestParams, CancellationToken, ValueTask<CallToolResult>> handler);

    /// <summary>
    /// Registers a tool interceptor that can modify, replace, or remove tools from the aggregated list.
    /// </summary>
    /// <param name="interceptor">The tool interceptor.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithToolInterceptor(IToolInterceptor interceptor);

    /// <summary>
    /// Registers a tool call interceptor that can intercept and modify tool calls.
    /// </summary>
    /// <param name="interceptor">The tool call interceptor.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithToolCallInterceptor(IToolCallInterceptor interceptor);

    /// <summary>
    /// Configures the proxy server information.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <param name="version">The server version.</param>
    /// <param name="instructions">Optional server instructions.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithServerInfo(string name, string version, string? instructions = null);

    /// <summary>
    /// Configures caching for tool lists.
    /// </summary>
    /// <param name="enabled">Whether caching is enabled.</param>
    /// <param name="ttlSeconds">Cache TTL in seconds.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithToolCaching(bool enabled, int ttlSeconds = 300);

    /// <summary>
    /// Loads configuration from a JSON file and applies it.
    /// Code-based configuration takes precedence over file-based configuration.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <returns>The builder for chaining.</returns>
    IMcpProxyBuilder WithConfigurationFile(string configPath);
}

/// <summary>
/// Builder interface for configuring an individual backend server.
/// </summary>
public interface IServerBuilder
{
    /// <summary>
    /// Sets the display title for this server.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithTitle(string title);

    /// <summary>
    /// Sets the description for this server.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithDescription(string description);

    /// <summary>
    /// Sets environment variables for the server process (STDIO only).
    /// </summary>
    /// <param name="environment">Environment variables.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithEnvironment(Dictionary<string, string> environment);

    /// <summary>
    /// Sets HTTP headers for requests to the server (HTTP/SSE only).
    /// </summary>
    /// <param name="headers">HTTP headers.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithHeaders(Dictionary<string, string> headers);

    /// <summary>
    /// Configures the route path for this server (PerServer routing mode).
    /// </summary>
    /// <param name="route">The route path.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithRoute(string route);

    /// <summary>
    /// Configures tool prefix for this server.
    /// </summary>
    /// <param name="prefix">The prefix to add to tool names.</param>
    /// <param name="separator">The separator between prefix and name.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithToolPrefix(string prefix, string separator = "_");

    /// <summary>
    /// Configures resource prefix for this server.
    /// </summary>
    /// <param name="prefix">The prefix to add to resource URIs.</param>
    /// <param name="separator">The separator between prefix and URI.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithResourcePrefix(string prefix, string separator = "://");

    /// <summary>
    /// Configures prompt prefix for this server.
    /// </summary>
    /// <param name="prefix">The prefix to add to prompt names.</param>
    /// <param name="separator">The separator between prefix and name.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithPromptPrefix(string prefix, string separator = "_");

    /// <summary>
    /// Configures an allow list filter for tools.
    /// </summary>
    /// <param name="patterns">Patterns to allow (supports wildcards).</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder AllowTools(params string[] patterns);

    /// <summary>
    /// Configures a deny list filter for tools.
    /// </summary>
    /// <param name="patterns">Patterns to deny (supports wildcards).</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder DenyTools(params string[] patterns);

    /// <summary>
    /// Configures a custom tool filter.
    /// </summary>
    /// <param name="filter">The filter instance.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithToolFilter(IToolFilter filter);

    /// <summary>
    /// Configures a custom tool transformer.
    /// </summary>
    /// <param name="transformer">The transformer instance.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithToolTransformer(IToolTransformer transformer);

    /// <summary>
    /// Adds a pre-invoke hook for this server.
    /// </summary>
    /// <param name="hook">The hook instance.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithPreInvokeHook(IPreInvokeHook hook);

    /// <summary>
    /// Adds a post-invoke hook for this server.
    /// </summary>
    /// <param name="hook">The hook instance.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithPostInvokeHook(IPostInvokeHook hook);

    /// <summary>
    /// Adds a combined tool hook for this server.
    /// </summary>
    /// <param name="hook">The hook instance.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder WithHook(IToolHook hook);

    /// <summary>
    /// Enables or disables this server.
    /// </summary>
    /// <param name="enabled">Whether the server is enabled.</param>
    /// <returns>The builder for chaining.</returns>
    IServerBuilder Enabled(bool enabled = true);

    /// <summary>
    /// Returns to the parent proxy builder.
    /// </summary>
    /// <returns>The proxy builder.</returns>
    IMcpProxyBuilder Build();
}

/// <summary>
/// Intercepts and can modify the tool list during aggregation.
/// </summary>
public interface IToolInterceptor
{
    /// <summary>
    /// Intercepts the tool list, allowing modification, addition, or removal of tools.
    /// </summary>
    /// <param name="tools">The current list of tools with server info.</param>
    /// <returns>The modified list of tools.</returns>
    IEnumerable<ToolWithServer> InterceptTools(IEnumerable<ToolWithServer> tools);
}

/// <summary>
/// Intercepts tool calls before routing to backend servers.
/// </summary>
public interface IToolCallInterceptor
{
    /// <summary>
    /// Gets the priority of this interceptor. Lower values execute first.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Intercepts a tool call. Return null to continue normal routing, or return a result to short-circuit.
    /// </summary>
    /// <param name="context">The tool call context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result if handled, or null to continue normal routing.</returns>
    ValueTask<CallToolResult?> InterceptAsync(ToolCallContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context for tool call interception.
/// </summary>
public sealed class ToolCallContext
{
    /// <summary>
    /// Gets the name of the tool being called (potentially prefixed).
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the original (unprefixed) tool name.
    /// </summary>
    public required string OriginalToolName { get; init; }

    /// <summary>
    /// Gets the server name that owns this tool.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets or sets the request parameters.
    /// </summary>
    public required CallToolRequestParams Request { get; set; }

    /// <summary>
    /// Gets a dictionary for sharing data between interceptors.
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// Gets the authentication result if available.
    /// </summary>
    public AuthenticationResult? AuthenticationResult { get; init; }
}

/// <summary>
/// Represents a tool with its source server information.
/// </summary>
public sealed class ToolWithServer
{
    /// <summary>
    /// Gets or sets the tool definition.
    /// </summary>
    public required Tool Tool { get; set; }

    /// <summary>
    /// Gets the original tool name (before any transformation).
    /// </summary>
    public required string OriginalName { get; init; }

    /// <summary>
    /// Gets the server name providing this tool.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets or sets whether this tool should be included.
    /// Set to false to filter out the tool.
    /// </summary>
    public bool Include { get; set; } = true;
}
