using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Filtering;
using McpProxy.Core.Hooks;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProxy.Core.Proxy;

/// <summary>
/// Information about a tool and its source server.
/// </summary>
public sealed class ToolInfo
{
    /// <summary>
    /// Gets the tool definition.
    /// </summary>
    public required Tool Tool { get; init; }

    /// <summary>
    /// Gets the original (unprefixed) tool name.
    /// </summary>
    public required string OriginalName { get; init; }

    /// <summary>
    /// Gets the name of the server providing this tool.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets the client info for the server.
    /// </summary>
    public required McpClientInfo ClientInfo { get; init; }
}

/// <summary>
/// Information about a resource and its source server.
/// </summary>
public sealed class ResourceInfo
{
    /// <summary>
    /// Gets the resource definition.
    /// </summary>
    public required Resource Resource { get; init; }

    /// <summary>
    /// Gets the name of the server providing this resource.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets the client info for the server.
    /// </summary>
    public required McpClientInfo ClientInfo { get; init; }
}

/// <summary>
/// Information about a prompt and its source server.
/// </summary>
public sealed class PromptInfo
{
    /// <summary>
    /// Gets the prompt definition.
    /// </summary>
    public required Prompt Prompt { get; init; }

    /// <summary>
    /// Gets the name of the server providing this prompt.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Gets the client info for the server.
    /// </summary>
    public required McpClientInfo ClientInfo { get; init; }
}

/// <summary>
/// Core proxy server that aggregates multiple MCP backends.
/// </summary>
public sealed class McpProxyServer
{
    private readonly ILogger<McpProxyServer> _logger;
    private readonly McpClientManager _clientManager;
    private readonly ProxyConfiguration _configuration;
    private readonly Dictionary<string, HookPipeline> _hookPipelines = [];
    private readonly Dictionary<string, IToolFilter> _filters = [];
    private readonly Dictionary<string, IToolTransformer> _transformers = [];
    private readonly Dictionary<string, ToolPrefixer?> _prefixers = [];

    /// <summary>
    /// Initializes a new instance of <see cref="McpProxyServer"/>.
    /// </summary>
    public McpProxyServer(
        ILogger<McpProxyServer> logger,
        McpClientManager clientManager,
        ProxyConfiguration configuration)
    {
        _logger = logger;
        _clientManager = clientManager;
        _configuration = configuration;

        InitializeFiltersAndTransformers();
    }

    private void InitializeFiltersAndTransformers()
    {
        foreach (var (name, serverConfig) in _configuration.Mcp)
        {
            _filters[name] = FilterFactory.Create(serverConfig.Tools.Filter);
            _transformers[name] = TransformerFactory.Create(serverConfig.Tools);

            // Store prefixer separately for reverse lookup
            _prefixers[name] = !string.IsNullOrEmpty(serverConfig.Tools.Prefix)
                ? new ToolPrefixer(serverConfig.Tools.Prefix, serverConfig.Tools.PrefixSeparator)
                : null;
        }
    }

    /// <summary>
    /// Adds a hook pipeline for a specific server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <param name="pipeline">The hook pipeline.</param>
    public void AddHookPipeline(string serverName, HookPipeline pipeline)
    {
        _hookPipelines[serverName] = pipeline;
    }

    /// <summary>
    /// Gets the hook pipeline for a server.
    /// </summary>
    /// <param name="serverName">The server name.</param>
    /// <returns>The hook pipeline, or null if none configured.</returns>
    public HookPipeline? GetHookPipeline(string serverName)
    {
        return _hookPipelines.TryGetValue(serverName, out var pipeline) ? pipeline : null;
    }

    /// <summary>
    /// Lists all tools from all backend servers.
    /// </summary>
    public ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellationToken)
    {
        // Delegate to the core implementation (context.Params is not used)
        return ListToolsCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Lists all tools from all backend servers (testable overload).
    /// </summary>
    public async ValueTask<ListToolsResult> ListToolsCoreAsync(CancellationToken cancellationToken)
    {
        ProxyLogger.ListingTools(_logger, _clientManager.Clients.Count);

        var allTools = new List<Tool>();

        foreach (var (name, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var tools = await clientInfo.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var filter = _filters.GetValueOrDefault(name, NoFilter.Instance);
                var transformer = _transformers.GetValueOrDefault(name, NoTransform.Instance);

                var filteredCount = 0;
                foreach (var tool in tools)
                {
                    if (filter.ShouldInclude(tool, name))
                    {
                        var transformed = transformer.Transform(tool, name);
                        allTools.Add(transformed);
                    }
                    else
                    {
                        filteredCount++;
                    }
                }

                if (filteredCount > 0)
                {
                    var filterMode = _configuration.Mcp[name].Tools.Filter.Mode.ToString();
                    ProxyLogger.ToolsFiltered(_logger, filteredCount, name, filterMode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list tools from server '{ServerName}'", name);
            }
        }

        ProxyLogger.ToolsListed(_logger, allTools.Count);

        return new ListToolsResult { Tools = allTools };
    }

    /// <summary>
    /// Calls a tool on the appropriate backend server.
    /// </summary>
    public ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        // Delegate to the core implementation
        return CallToolCoreAsync(context.Params!, cancellationToken);
    }

    /// <summary>
    /// Calls a tool on the appropriate backend server (testable overload).
    /// </summary>
    public async ValueTask<CallToolResult> CallToolCoreAsync(
        CallToolRequestParams request,
        CancellationToken cancellationToken)
    {
        var toolName = request.Name;

        // Find the tool and its source server
        var toolInfo = await FindToolAsync(toolName, cancellationToken).ConfigureAwait(false);

        if (toolInfo is null)
        {
            ProxyLogger.ToolNotFound(_logger, toolName);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Tool '{toolName}' not found" }],
                IsError = true
            };
        }

        ProxyLogger.CallingTool(_logger, toolInfo.OriginalName, toolInfo.ServerName);

        // Create hook context - create a new CallToolRequestParams with the original name
        var modifiedRequest = new CallToolRequestParams
        {
            Name = toolInfo.OriginalName,
            Arguments = request.Arguments
        };

        var hookContext = new HookContext<CallToolRequestParams>
        {
            ServerName = toolInfo.ServerName,
            ToolName = toolInfo.OriginalName,
            Request = modifiedRequest,
            CancellationToken = cancellationToken
        };

        // Execute pre-invoke hooks
        var pipeline = GetHookPipeline(toolInfo.ServerName);
        if (pipeline is not null)
        {
            await pipeline.ExecutePreInvokeHooksAsync(hookContext).ConfigureAwait(false);
        }

        try
        {
            // Call the tool
            var arguments = hookContext.Request.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value);
            var result = await toolInfo.ClientInfo.Client.CallToolAsync(
                hookContext.Request.Name,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Execute post-invoke hooks
            if (pipeline is not null)
            {
                result = await pipeline.ExecutePostInvokeHooksAsync(hookContext, result).ConfigureAwait(false);
            }

            ProxyLogger.ToolCallCompleted(_logger, toolInfo.OriginalName);
            return result;
        }
        catch (Exception ex)
        {
            ProxyLogger.ToolCallFailed(_logger, toolInfo.OriginalName, ex);
            throw;
        }
    }

    /// <summary>
    /// Lists all resources from all backend servers.
    /// </summary>
    public async ValueTask<ListResourcesResult> ListResourcesAsync(
        RequestContext<ListResourcesRequestParams> context,
        CancellationToken cancellationToken)
    {
        ProxyLogger.ListingResources(_logger, _clientManager.Clients.Count);

        var allResources = new List<Resource>();

        foreach (var (name, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var resources = await clientInfo.Client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                allResources.AddRange(resources);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list resources from server '{ServerName}'", name);
            }
        }

        ProxyLogger.ResourcesListed(_logger, allResources.Count);

        return new ListResourcesResult { Resources = allResources };
    }

    /// <summary>
    /// Reads a resource from the appropriate backend server.
    /// </summary>
    public async ValueTask<ReadResourceResult> ReadResourceAsync(
        RequestContext<ReadResourceRequestParams> context,
        CancellationToken cancellationToken)
    {
        var uri = context.Params!.Uri;

        // Find the resource and its source server
        var resourceInfo = await FindResourceAsync(uri, cancellationToken).ConfigureAwait(false);

        if (resourceInfo is null)
        {
            ProxyLogger.ResourceNotFound(_logger, uri);
            return new ReadResourceResult
            {
                Contents = [new TextResourceContents { Uri = uri, Text = $"Resource '{uri}' not found" }]
            };
        }

        ProxyLogger.ReadingResource(_logger, uri, resourceInfo.ServerName);

        var result = await resourceInfo.ClientInfo.Client.ReadResourceAsync(uri, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ReadResourceResult { Contents = [.. result.Contents] };
    }

    /// <summary>
    /// Lists all prompts from all backend servers.
    /// </summary>
    public async ValueTask<ListPromptsResult> ListPromptsAsync(
        RequestContext<ListPromptsRequestParams> context,
        CancellationToken cancellationToken)
    {
        ProxyLogger.ListingPrompts(_logger, _clientManager.Clients.Count);

        var allPrompts = new List<Prompt>();

        foreach (var (name, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var prompts = await clientInfo.Client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                allPrompts.AddRange(prompts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list prompts from server '{ServerName}'", name);
            }
        }

        ProxyLogger.PromptsListed(_logger, allPrompts.Count);

        return new ListPromptsResult { Prompts = allPrompts };
    }

    /// <summary>
    /// Gets a prompt from the appropriate backend server.
    /// </summary>
    public async ValueTask<GetPromptResult> GetPromptAsync(
        RequestContext<GetPromptRequestParams> context,
        CancellationToken cancellationToken)
    {
        var promptName = context.Params!.Name;

        // Find the prompt and its source server
        var promptInfo = await FindPromptAsync(promptName, cancellationToken).ConfigureAwait(false);

        if (promptInfo is null)
        {
            return new GetPromptResult
            {
                Messages = [new PromptMessage { Role = Role.Assistant, Content = new TextContentBlock { Text = $"Prompt '{promptName}' not found" } }]
            };
        }

        ProxyLogger.GettingPrompt(_logger, promptName, promptInfo.ServerName);

        var arguments = context.Params.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value);
        var result = await promptInfo.ClientInfo.Client.GetPromptAsync(promptName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new GetPromptResult
        {
            Description = result.Description,
            Messages = result.Messages.Select(m => new PromptMessage
            {
                Role = m.Role,
                Content = m.Content switch
                {
                    TextContentBlock text => text,
                    _ => new TextContentBlock { Text = m.Content.ToString() ?? string.Empty }
                }
            }).ToList()
        };
    }

    /// <summary>
    /// Finds a tool by name across all backend servers.
    /// </summary>
    private async Task<ToolInfo?> FindToolAsync(string toolName, CancellationToken cancellationToken)
    {
        foreach (var (serverName, clientInfo) in _clientManager.Clients)
        {
            var prefixer = _prefixers.GetValueOrDefault(serverName);
            string originalName;

            if (prefixer is not null && prefixer.HasPrefix(toolName))
            {
                originalName = prefixer.RemovePrefix(toolName);
            }
            else
            {
                originalName = toolName;
            }

            try
            {
                var tools = await clientInfo.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var tool = tools.FirstOrDefault(t => string.Equals(t.Name, originalName, StringComparison.OrdinalIgnoreCase));

                if (tool is not null)
                {
                    return new ToolInfo
                    {
                        Tool = tool,
                        OriginalName = originalName,
                        ServerName = serverName,
                        ClientInfo = clientInfo
                    };
                }
            }
            catch
            {
                // Continue searching other servers
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a resource by URI across all backend servers.
    /// </summary>
    private async Task<ResourceInfo?> FindResourceAsync(string uri, CancellationToken cancellationToken)
    {
        foreach (var (serverName, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var resources = await clientInfo.Client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var resource = resources.FirstOrDefault(r => string.Equals(r.Uri, uri, StringComparison.OrdinalIgnoreCase));

                if (resource is not null)
                {
                    return new ResourceInfo
                    {
                        Resource = resource,
                        ServerName = serverName,
                        ClientInfo = clientInfo
                    };
                }
            }
            catch
            {
                // Continue searching other servers
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a prompt by name across all backend servers.
    /// </summary>
    private async Task<PromptInfo?> FindPromptAsync(string promptName, CancellationToken cancellationToken)
    {
        foreach (var (serverName, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var prompts = await clientInfo.Client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var prompt = prompts.FirstOrDefault(p => string.Equals(p.Name, promptName, StringComparison.OrdinalIgnoreCase));

                if (prompt is not null)
                {
                    return new PromptInfo
                    {
                        Prompt = prompt,
                        ServerName = serverName,
                        ClientInfo = clientInfo
                    };
                }
            }
            catch
            {
                // Continue searching other servers
            }
        }

        return null;
    }
}
