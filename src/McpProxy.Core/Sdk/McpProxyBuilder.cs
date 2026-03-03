using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Sdk;

/// <summary>
/// Builder for configuring MCP Proxy programmatically.
/// Provides a fluent API for SDK-style consumption of the proxy.
/// </summary>
public sealed class McpProxyBuilder : IMcpProxyBuilder
{
    private readonly ProxyConfiguration _configuration;
    private readonly List<IPreInvokeHook> _globalPreInvokeHooks = [];
    private readonly List<IPostInvokeHook> _globalPostInvokeHooks = [];
    private readonly List<VirtualToolDefinition> _virtualTools = [];
    private readonly List<IToolInterceptor> _toolInterceptors = [];
    private readonly List<IToolCallInterceptor> _toolCallInterceptors = [];
    private readonly Dictionary<string, ServerBuilderState> _serverBuilders = [];
    private string? _configFilePath;

    /// <summary>
    /// Initializes a new instance of <see cref="McpProxyBuilder"/>.
    /// </summary>
    public McpProxyBuilder()
    {
        _configuration = new ProxyConfiguration();
    }

    /// <summary>
    /// Creates a new MCP Proxy builder.
    /// </summary>
    /// <returns>A new builder instance.</returns>
    public static McpProxyBuilder Create() => new();

    /// <inheritdoc />
    public IServerBuilder AddStdioServer(string name, string command, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var serverConfig = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = command,
            Arguments = arguments.Length > 0 ? arguments : null
        };

        _configuration.Mcp[name] = serverConfig;
        var builderState = new ServerBuilderState(name, serverConfig);
        _serverBuilders[name] = builderState;

        return new ServerBuilder(this, builderState);
    }

    /// <inheritdoc />
    public IServerBuilder AddHttpServer(string name, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var serverConfig = new ServerConfiguration
        {
            Type = ServerTransportType.Http,
            Url = url
        };

        _configuration.Mcp[name] = serverConfig;
        var builderState = new ServerBuilderState(name, serverConfig);
        _serverBuilders[name] = builderState;

        return new ServerBuilder(this, builderState);
    }

    /// <inheritdoc />
    public IServerBuilder AddSseServer(string name, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var serverConfig = new ServerConfiguration
        {
            Type = ServerTransportType.Sse,
            Url = url
        };

        _configuration.Mcp[name] = serverConfig;
        var builderState = new ServerBuilderState(name, serverConfig);
        _serverBuilders[name] = builderState;

        return new ServerBuilder(this, builderState);
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithGlobalPreInvokeHook(IPreInvokeHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _globalPreInvokeHooks.Add(hook);
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithGlobalPostInvokeHook(IPostInvokeHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _globalPostInvokeHooks.Add(hook);
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithGlobalHook(IToolHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _globalPreInvokeHooks.Add(hook);
        _globalPostInvokeHooks.Add(hook);
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder AddVirtualTool(
        Tool tool,
        Func<CallToolRequestParams, CancellationToken, ValueTask<CallToolResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(handler);

        _virtualTools.Add(new VirtualToolDefinition
        {
            Tool = tool,
            Handler = handler
        });

        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithToolInterceptor(IToolInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _toolInterceptors.Add(interceptor);
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithToolCallInterceptor(IToolCallInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _toolCallInterceptors.Add(interceptor);
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithServerInfo(string name, string version, string? instructions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        _configuration.Proxy.ServerInfo.Name = name;
        _configuration.Proxy.ServerInfo.Version = version;
        _configuration.Proxy.ServerInfo.Instructions = instructions;

        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithToolCaching(bool enabled, int ttlSeconds = 300)
    {
        _configuration.Proxy.Caching.Tools.Enabled = enabled;
        _configuration.Proxy.Caching.Tools.TtlSeconds = ttlSeconds;
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder WithConfigurationFile(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        _configFilePath = configPath;
        return this;
    }

    /// <summary>
    /// Builds the SDK configuration containing all settings.
    /// </summary>
    /// <returns>The SDK configuration.</returns>
    public McpProxySdkConfiguration BuildConfiguration()
    {
        return new McpProxySdkConfiguration
        {
            Configuration = _configuration,
            ConfigFilePath = _configFilePath,
            GlobalPreInvokeHooks = [.. _globalPreInvokeHooks],
            GlobalPostInvokeHooks = [.. _globalPostInvokeHooks],
            VirtualTools = [.. _virtualTools],
            ToolInterceptors = [.. _toolInterceptors],
            ToolCallInterceptors = [.. _toolCallInterceptors],
            ServerStates = _serverBuilders.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToServerState())
        };
    }

    /// <summary>
    /// Gets the proxy configuration.
    /// </summary>
    internal ProxyConfiguration Configuration => _configuration;
}

/// <summary>
/// Internal state for server configuration during building.
/// </summary>
internal sealed class ServerBuilderState
{
    public string Name { get; }
    public ServerConfiguration Configuration { get; }
    public List<IPreInvokeHook> PreInvokeHooks { get; } = [];
    public List<IPostInvokeHook> PostInvokeHooks { get; } = [];
    public IToolFilter? CustomFilter { get; set; }
    public IToolTransformer? CustomTransformer { get; set; }

    public ServerBuilderState(string name, ServerConfiguration configuration)
    {
        Name = name;
        Configuration = configuration;
    }

    public ServerState ToServerState() => new()
    {
        Name = Name,
        Configuration = Configuration,
        PreInvokeHooks = [.. PreInvokeHooks],
        PostInvokeHooks = [.. PostInvokeHooks],
        CustomFilter = CustomFilter,
        CustomTransformer = CustomTransformer
    };
}

/// <summary>
/// Builder for configuring an individual server.
/// </summary>
internal sealed class ServerBuilder : IServerBuilder
{
    private readonly McpProxyBuilder _parent;
    private readonly ServerBuilderState _state;

    public ServerBuilder(McpProxyBuilder parent, ServerBuilderState state)
    {
        _parent = parent;
        _state = state;
    }

    /// <inheritdoc />
    public IServerBuilder WithTitle(string title)
    {
        _state.Configuration.Title = title;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithDescription(string description)
    {
        _state.Configuration.Description = description;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithEnvironment(Dictionary<string, string> environment)
    {
        _state.Configuration.Environment = environment;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithHeaders(Dictionary<string, string> headers)
    {
        _state.Configuration.Headers = headers;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithRoute(string route)
    {
        _state.Configuration.Route = route;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithToolPrefix(string prefix, string separator = "_")
    {
        _state.Configuration.Tools.Prefix = prefix;
        _state.Configuration.Tools.PrefixSeparator = separator;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithResourcePrefix(string prefix, string separator = "://")
    {
        _state.Configuration.Resources.Prefix = prefix;
        _state.Configuration.Resources.PrefixSeparator = separator;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithPromptPrefix(string prefix, string separator = "_")
    {
        _state.Configuration.Prompts.Prefix = prefix;
        _state.Configuration.Prompts.PrefixSeparator = separator;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder AllowTools(params string[] patterns)
    {
        _state.Configuration.Tools.Filter.Mode = FilterMode.AllowList;
        _state.Configuration.Tools.Filter.Patterns = patterns;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder DenyTools(params string[] patterns)
    {
        _state.Configuration.Tools.Filter.Mode = FilterMode.DenyList;
        _state.Configuration.Tools.Filter.Patterns = patterns;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithToolFilter(IToolFilter filter)
    {
        _state.CustomFilter = filter;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithToolTransformer(IToolTransformer transformer)
    {
        _state.CustomTransformer = transformer;
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithPreInvokeHook(IPreInvokeHook hook)
    {
        _state.PreInvokeHooks.Add(hook);
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithPostInvokeHook(IPostInvokeHook hook)
    {
        _state.PostInvokeHooks.Add(hook);
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder WithHook(IToolHook hook)
    {
        _state.PreInvokeHooks.Add(hook);
        _state.PostInvokeHooks.Add(hook);
        return this;
    }

    /// <inheritdoc />
    public IServerBuilder Enabled(bool enabled = true)
    {
        _state.Configuration.Enabled = enabled;
        return this;
    }

    /// <inheritdoc />
    public IMcpProxyBuilder Build()
    {
        return _parent;
    }
}

/// <summary>
/// Configuration built from the SDK builder.
/// </summary>
public sealed class McpProxySdkConfiguration
{
    /// <summary>
    /// Gets the base proxy configuration.
    /// </summary>
    public required ProxyConfiguration Configuration { get; init; }

    /// <summary>
    /// Gets the optional path to a configuration file to merge.
    /// </summary>
    public string? ConfigFilePath { get; init; }

    /// <summary>
    /// Gets the global pre-invoke hooks.
    /// </summary>
    public required IReadOnlyList<IPreInvokeHook> GlobalPreInvokeHooks { get; init; }

    /// <summary>
    /// Gets the global post-invoke hooks.
    /// </summary>
    public required IReadOnlyList<IPostInvokeHook> GlobalPostInvokeHooks { get; init; }

    /// <summary>
    /// Gets the virtual tools.
    /// </summary>
    public required IReadOnlyList<VirtualToolDefinition> VirtualTools { get; init; }

    /// <summary>
    /// Gets the tool interceptors.
    /// </summary>
    public required IReadOnlyList<IToolInterceptor> ToolInterceptors { get; init; }

    /// <summary>
    /// Gets the tool call interceptors.
    /// </summary>
    public required IReadOnlyList<IToolCallInterceptor> ToolCallInterceptors { get; init; }

    /// <summary>
    /// Gets the per-server states.
    /// </summary>
    public required IReadOnlyDictionary<string, ServerState> ServerStates { get; init; }
}

/// <summary>
/// Immutable state for a server configuration.
/// </summary>
public sealed class ServerState
{
    /// <summary>
    /// Gets the server name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the server configuration.
    /// </summary>
    public required ServerConfiguration Configuration { get; init; }

    /// <summary>
    /// Gets the pre-invoke hooks for this server.
    /// </summary>
    public required IReadOnlyList<IPreInvokeHook> PreInvokeHooks { get; init; }

    /// <summary>
    /// Gets the post-invoke hooks for this server.
    /// </summary>
    public required IReadOnlyList<IPostInvokeHook> PostInvokeHooks { get; init; }

    /// <summary>
    /// Gets the custom tool filter if configured.
    /// </summary>
    public IToolFilter? CustomFilter { get; init; }

    /// <summary>
    /// Gets the custom tool transformer if configured.
    /// </summary>
    public IToolTransformer? CustomTransformer { get; init; }
}

/// <summary>
/// Definition of a virtual tool handled by the proxy.
/// </summary>
public sealed class VirtualToolDefinition
{
    /// <summary>
    /// Gets the tool definition.
    /// </summary>
    public required Tool Tool { get; init; }

    /// <summary>
    /// Gets the handler function.
    /// </summary>
    public required Func<CallToolRequestParams, CancellationToken, ValueTask<CallToolResult>> Handler { get; init; }
}
