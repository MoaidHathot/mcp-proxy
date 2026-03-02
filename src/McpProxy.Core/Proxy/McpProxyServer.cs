using McpProxy.Abstractions;
using McpProxy.Core.Caching;
using McpProxy.Core.Configuration;
using McpProxy.Core.Filtering;
using McpProxy.Core.Hooks;
using McpProxy.Core.Hooks.BuiltIn;
using McpProxy.Core.Logging;
using Microsoft.AspNetCore.Http;
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
    /// Gets the original (unprefixed) resource URI.
    /// </summary>
    public required string OriginalUri { get; init; }

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
    /// Gets the original (unprefixed) prompt name.
    /// </summary>
    public required string OriginalName { get; init; }

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
    private readonly IToolCache _toolCache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly int _cacheTtlSeconds;
    private readonly Dictionary<string, HookPipeline> _hookPipelines = [];
    private readonly Dictionary<string, IToolFilter> _toolFilters = [];
    private readonly Dictionary<string, IResourceFilter> _resourceFilters = [];
    private readonly Dictionary<string, IPromptFilter> _promptFilters = [];
    private readonly Dictionary<string, IToolTransformer> _transformers = [];
    private readonly Dictionary<string, IResourceTransformer> _resourceTransformers = [];
    private readonly Dictionary<string, IPromptTransformer> _promptTransformers = [];
    private readonly Dictionary<string, ToolPrefixer?> _prefixers = [];
    private readonly Dictionary<string, ResourcePrefixer?> _resourcePrefixers = [];
    private readonly Dictionary<string, PromptPrefixer?> _promptPrefixers = [];

    /// <summary>
    /// Initializes a new instance of <see cref="McpProxyServer"/>.
    /// </summary>
    public McpProxyServer(
        ILogger<McpProxyServer> logger,
        McpClientManager clientManager,
        ProxyConfiguration configuration)
        : this(logger, clientManager, configuration, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="McpProxyServer"/> with a custom tool cache.
    /// </summary>
    public McpProxyServer(
        ILogger<McpProxyServer> logger,
        McpClientManager clientManager,
        ProxyConfiguration configuration,
        IToolCache? toolCache,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _logger = logger;
        _clientManager = clientManager;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;

        var cacheConfig = configuration.Proxy.Caching.Tools;
        _cacheTtlSeconds = cacheConfig.TtlSeconds;
        _toolCache = toolCache ?? (cacheConfig.Enabled
            ? new ToolCache(cacheConfig.TtlSeconds)
            : NullToolCache.Instance);

        InitializeFiltersAndTransformers();
    }

    private void InitializeFiltersAndTransformers()
    {
        foreach (var (name, serverConfig) in _configuration.Mcp)
        {
            _toolFilters[name] = FilterFactory.Create(serverConfig.Tools.Filter);
            _resourceFilters[name] = ResourceFilterFactory.Create(serverConfig.Resources.Filter);
            _promptFilters[name] = PromptFilterFactory.Create(serverConfig.Prompts.Filter);
            _transformers[name] = TransformerFactory.Create(serverConfig.Tools);
            _resourceTransformers[name] = ResourceTransformerFactory.Create(serverConfig.Resources);
            _promptTransformers[name] = PromptTransformerFactory.Create(serverConfig.Prompts);

            // Store prefixer separately for reverse lookup
            _prefixers[name] = !string.IsNullOrEmpty(serverConfig.Tools.Prefix)
                ? new ToolPrefixer(serverConfig.Tools.Prefix, serverConfig.Tools.PrefixSeparator)
                : null;

            _resourcePrefixers[name] = !string.IsNullOrEmpty(serverConfig.Resources.Prefix)
                ? new ResourcePrefixer(serverConfig.Resources.Prefix, serverConfig.Resources.PrefixSeparator)
                : null;

            _promptPrefixers[name] = !string.IsNullOrEmpty(serverConfig.Prompts.Prefix)
                ? new PromptPrefixer(serverConfig.Prompts.Prefix, serverConfig.Prompts.PrefixSeparator)
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

                // Cache the raw tools from the server
                CacheToolsForServer(name, tools);

                var filter = _toolFilters.GetValueOrDefault(name, NoFilter.Instance);
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

        // Get authentication result from HttpContext if available
        var authResult = GetAuthenticationResult();

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
            CancellationToken = cancellationToken,
            AuthenticationResult = authResult
        };

        // Execute pre-invoke hooks
        var pipeline = GetHookPipeline(toolInfo.ServerName);
        if (pipeline is not null)
        {
            await pipeline.ExecutePreInvokeHooksAsync(hookContext).ConfigureAwait(false);
        }

        // Use the potentially modified cancellation token from hooks (e.g., TimeoutHook)
        var effectiveCancellationToken = hookContext.CancellationToken;

        CallToolResult result;
        try
        {
            // Call the tool with retry support
            result = await ExecuteToolCallWithRetryAsync(
                toolInfo,
                hookContext,
                pipeline,
                effectiveCancellationToken).ConfigureAwait(false);

            ProxyLogger.ToolCallCompleted(_logger, toolInfo.OriginalName);
            return result;
        }
        catch (Exception ex)
        {
            ProxyLogger.ToolCallFailed(_logger, toolInfo.OriginalName, ex);
            throw;
        }
        finally
        {
            // Dispose the timeout CTS if it was created
            if (hookContext.Items.TryGetValue(TimeoutHook.TimeoutCtsKey, out var ctsObj) && 
                ctsObj is CancellationTokenSource cts)
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Executes a tool call with retry support.
    /// </summary>
    private async Task<CallToolResult> ExecuteToolCallWithRetryAsync(
        ToolInfo toolInfo,
        HookContext<CallToolRequestParams> hookContext,
        HookPipeline? pipeline,
        CancellationToken cancellationToken)
    {
        const int MaxRetryIterations = 10; // Safety limit to prevent infinite loops
        
        for (var iteration = 0; iteration < MaxRetryIterations; iteration++)
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

            // Check if retry was requested by the RetryHook
            if (!hookContext.Items.TryGetValue(RetryHook.RetryRequestedKey, out var retryRequestedObj) ||
                retryRequestedObj is not true)
            {
                // No retry requested, return the result
                return result;
            }

            // Get retry delay
            var delayMs = 0;
            if (hookContext.Items.TryGetValue(RetryHook.RetryDelayMsKey, out var delayObj) && delayObj is int delay)
            {
                delayMs = delay;
            }

            // Clear the retry request flag before next iteration
            hookContext.Items[RetryHook.RetryRequestedKey] = false;

            // Wait before retrying
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            // Continue to next iteration (retry)
        }

        // Should not reach here normally, but return an error if we exhaust iterations
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Maximum retry iterations exceeded" }],
            IsError = true
        };
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
                var filter = _resourceFilters.GetValueOrDefault(name, NoResourceFilter.Instance);
                var transformer = _resourceTransformers.GetValueOrDefault(name, NoResourceTransform.Instance);

                var filteredCount = 0;
                foreach (var resource in resources)
                {
                    if (filter.ShouldInclude(resource, name))
                    {
                        var transformed = transformer.Transform(resource, name);
                        allResources.Add(transformed);
                    }
                    else
                    {
                        filteredCount++;
                    }
                }

                if (filteredCount > 0)
                {
                    ProxyLogger.ResourcesFiltered(_logger, filteredCount, name);
                }
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

        ProxyLogger.ReadingResource(_logger, resourceInfo.OriginalUri, resourceInfo.ServerName);

        var result = await resourceInfo.ClientInfo.Client.ReadResourceAsync(resourceInfo.OriginalUri, cancellationToken: cancellationToken).ConfigureAwait(false);

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
                var filter = _promptFilters.GetValueOrDefault(name, NoPromptFilter.Instance);
                var transformer = _promptTransformers.GetValueOrDefault(name, NoPromptTransform.Instance);

                var filteredCount = 0;
                foreach (var prompt in prompts)
                {
                    if (filter.ShouldInclude(prompt, name))
                    {
                        var transformed = transformer.Transform(prompt, name);
                        allPrompts.Add(transformed);
                    }
                    else
                    {
                        filteredCount++;
                    }
                }

                if (filteredCount > 0)
                {
                    ProxyLogger.PromptsFiltered(_logger, filteredCount, name);
                }
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

        ProxyLogger.GettingPrompt(_logger, promptInfo.OriginalName, promptInfo.ServerName);

        var arguments = context.Params.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value);
        var result = await promptInfo.ClientInfo.Client.GetPromptAsync(promptInfo.OriginalName, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);

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
        // Try cache first
        var cachedTool = _toolCache.GetTool(toolName);
        if (cachedTool is not null)
        {
            ProxyLogger.ToolCacheHit(_logger, toolName, cachedTool.ServerName);

            // We have cached tool info, but we need to get the client info
            if (_clientManager.Clients.TryGetValue(cachedTool.ServerName, out var cachedClientInfo))
            {
                return new ToolInfo
                {
                    Tool = cachedTool.Tool,
                    OriginalName = cachedTool.OriginalName,
                    ServerName = cachedTool.ServerName,
                    ClientInfo = cachedClientInfo
                };
            }
        }

        ProxyLogger.ToolCacheMiss(_logger, toolName);

        // Cache miss - search all servers and update cache
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
                // Check if we have valid cache for this server
                var cachedTools = _toolCache.GetToolsForServer(serverName);
                IList<Tool> tools;

                if (cachedTools is not null)
                {
                    tools = cachedTools;
                }
                else
                {
                    // Fetch from backend and cache
                    tools = await clientInfo.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    CacheToolsForServer(serverName, tools);
                }

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
    /// Caches tools for a server with prefixed name mappings.
    /// </summary>
    private void CacheToolsForServer(string serverName, IList<Tool> tools)
    {
        var prefixer = _prefixers.GetValueOrDefault(serverName);
        Dictionary<string, string>? prefixedNames = null;

        if (prefixer is not null)
        {
            prefixedNames = [];
            foreach (var tool in tools)
            {
                var prefixedName = prefixer.AddPrefix(tool.Name);
                prefixedNames[prefixedName] = tool.Name;
            }
        }

        _toolCache.SetToolsForServer(serverName, tools, prefixedNames);
        ProxyLogger.ToolsCached(_logger, tools.Count, serverName, _cacheTtlSeconds);
    }

    /// <summary>
    /// Invalidates the tool cache for a specific server.
    /// </summary>
    public void InvalidateToolCache(string serverName)
    {
        _toolCache.InvalidateServer(serverName);
        ProxyLogger.ToolCacheInvalidated(_logger, serverName);
    }

    /// <summary>
    /// Invalidates all tool caches.
    /// </summary>
    public void InvalidateAllToolCaches()
    {
        _toolCache.InvalidateAll();
        ProxyLogger.AllToolCachesInvalidated(_logger);
    }

    /// <summary>
    /// Finds a resource by URI across all backend servers.
    /// </summary>
    private async Task<ResourceInfo?> FindResourceAsync(string uri, CancellationToken cancellationToken)
    {
        foreach (var (serverName, clientInfo) in _clientManager.Clients)
        {
            var prefixer = _resourcePrefixers.GetValueOrDefault(serverName);
            string originalUri;

            if (prefixer is not null && prefixer.HasPrefix(uri))
            {
                originalUri = prefixer.RemovePrefix(uri);
            }
            else
            {
                originalUri = uri;
            }

            try
            {
                var resources = await clientInfo.Client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var resource = resources.FirstOrDefault(r => string.Equals(r.Uri, originalUri, StringComparison.OrdinalIgnoreCase));

                if (resource is not null)
                {
                    return new ResourceInfo
                    {
                        Resource = resource,
                        OriginalUri = originalUri,
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
            var prefixer = _promptPrefixers.GetValueOrDefault(serverName);
            string originalName;

            if (prefixer is not null && prefixer.HasPrefix(promptName))
            {
                originalName = prefixer.RemovePrefix(promptName);
            }
            else
            {
                originalName = promptName;
            }

            try
            {
                var prompts = await clientInfo.Client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var prompt = prompts.FirstOrDefault(p => string.Equals(p.Name, originalName, StringComparison.OrdinalIgnoreCase));

                if (prompt is not null)
                {
                    return new PromptInfo
                    {
                        Prompt = prompt,
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
    /// Subscribes to a resource on the appropriate backend server.
    /// </summary>
    /// <param name="context">The request context containing subscription parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty result indicating success or an error.</returns>
    public ValueTask<EmptyResult> SubscribeToResourceAsync(
        RequestContext<SubscribeRequestParams> context,
        CancellationToken cancellationToken)
    {
        return SubscribeToResourceCoreAsync(context.Params!.Uri, cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the appropriate backend server (testable overload).
    /// </summary>
    /// <param name="uri">The resource URI to subscribe to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty result indicating success or an error.</returns>
    public async ValueTask<EmptyResult> SubscribeToResourceCoreAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        ProxyLogger.SubscribingToResource(_logger, uri);

        // Find which server owns this resource
        var resourceInfo = await FindResourceAsync(uri, cancellationToken).ConfigureAwait(false);

        if (resourceInfo is null)
        {
            ProxyLogger.ResourceNotFoundForSubscription(_logger, uri);
            // Return empty result - subscription to non-existent resource is typically ignored
            return new EmptyResult();
        }

        try
        {
            await resourceInfo.ClientInfo.Client.SubscribeToResourceAsync(resourceInfo.OriginalUri, cancellationToken).ConfigureAwait(false);
            ProxyLogger.SubscribedToResource(_logger, resourceInfo.OriginalUri, resourceInfo.ServerName);
        }
        catch (Exception ex)
        {
            ProxyLogger.ResourceSubscriptionFailed(_logger, resourceInfo.OriginalUri, resourceInfo.ServerName, ex);
            throw;
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Unsubscribes from a resource on the appropriate backend server.
    /// </summary>
    /// <param name="context">The request context containing unsubscription parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty result indicating success or an error.</returns>
    public ValueTask<EmptyResult> UnsubscribeFromResourceAsync(
        RequestContext<UnsubscribeRequestParams> context,
        CancellationToken cancellationToken)
    {
        return UnsubscribeFromResourceCoreAsync(context.Params!.Uri, cancellationToken);
    }

    /// <summary>
    /// Unsubscribes from a resource on the appropriate backend server (testable overload).
    /// </summary>
    /// <param name="uri">The resource URI to unsubscribe from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty result indicating success or an error.</returns>
    public async ValueTask<EmptyResult> UnsubscribeFromResourceCoreAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        ProxyLogger.UnsubscribingFromResource(_logger, uri);

        // Find which server owns this resource
        var resourceInfo = await FindResourceAsync(uri, cancellationToken).ConfigureAwait(false);

        if (resourceInfo is null)
        {
            ProxyLogger.NoSubscriptionToUnsubscribe(_logger, uri);
            // Return empty result - unsubscription from non-existent resource is typically ignored
            return new EmptyResult();
        }

        try
        {
            await resourceInfo.ClientInfo.Client.UnsubscribeFromResourceAsync(resourceInfo.OriginalUri, cancellationToken).ConfigureAwait(false);
            ProxyLogger.UnsubscribedFromResource(_logger, resourceInfo.OriginalUri, resourceInfo.ServerName);
        }
        catch (Exception ex)
        {
            ProxyLogger.ResourceUnsubscriptionFailed(_logger, resourceInfo.OriginalUri, resourceInfo.ServerName, ex);
            throw;
        }

        return new EmptyResult();
    }

    private AuthenticationResult? GetAuthenticationResult()
    {
        if (_httpContextAccessor?.HttpContext is null)
        {
            return null;
        }

        if (_httpContextAccessor.HttpContext.Items.TryGetValue("McpProxy.Authentication.Result", out var result) 
            && result is AuthenticationResult authResult)
        {
            return authResult;
        }

        return null;
    }
}
