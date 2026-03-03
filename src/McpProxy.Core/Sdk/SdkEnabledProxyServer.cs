using McpProxy.Abstractions;
using McpProxy.Core.Caching;
using McpProxy.Core.Configuration;
using McpProxy.Core.Filtering;
using McpProxy.Core.Hooks;
using McpProxy.Core.Hooks.BuiltIn;
using McpProxy.Core.Logging;
using McpProxy.Core.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProxy.Core.Sdk;

/// <summary>
/// Extended proxy server that supports SDK features like virtual tools and interceptors.
/// </summary>
public sealed class SdkEnabledProxyServer
{
    private readonly ILogger<SdkEnabledProxyServer> _logger;
    private readonly McpClientManager _clientManager;
    private readonly ProxyConfiguration _configuration;
    private readonly McpProxySdkConfiguration _sdkConfiguration;
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
    private readonly HookPipeline? _globalHookPipeline;

    /// <summary>
    /// Initializes a new instance of <see cref="SdkEnabledProxyServer"/>.
    /// </summary>
    public SdkEnabledProxyServer(
        ILogger<SdkEnabledProxyServer> logger,
        McpClientManager clientManager,
        McpProxySdkConfiguration sdkConfiguration,
        IToolCache? toolCache = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _logger = logger;
        _clientManager = clientManager;
        _configuration = sdkConfiguration.Configuration;
        _sdkConfiguration = sdkConfiguration;
        _httpContextAccessor = httpContextAccessor;

        var cacheConfig = _configuration.Proxy.Caching.Tools;
        _cacheTtlSeconds = cacheConfig.TtlSeconds;
        _toolCache = toolCache ?? (cacheConfig.Enabled
            ? new ToolCache(cacheConfig.TtlSeconds)
            : NullToolCache.Instance);

        InitializeFiltersAndTransformers();

        // Create global hook pipeline if there are global hooks
        if (sdkConfiguration.GlobalPreInvokeHooks.Count > 0 ||
            sdkConfiguration.GlobalPostInvokeHooks.Count > 0)
        {
            _globalHookPipeline = CreateGlobalHookPipeline(sdkConfiguration);
        }
    }

    private HookPipeline CreateGlobalHookPipeline(McpProxySdkConfiguration config)
    {
        var pipeline = new HookPipeline(
            _logger as ILogger<HookPipeline> ?? 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HookPipeline>.Instance);

        foreach (var hook in config.GlobalPreInvokeHooks)
        {
            pipeline.AddPreInvokeHook(hook);
        }

        foreach (var hook in config.GlobalPostInvokeHooks)
        {
            pipeline.AddPostInvokeHook(hook);
        }

        return pipeline;
    }

    private void InitializeFiltersAndTransformers()
    {
        foreach (var (name, serverConfig) in _configuration.Mcp)
        {
            // Check if there's a custom filter/transformer from SDK
            var serverState = _sdkConfiguration.ServerStates.GetValueOrDefault(name);

            _toolFilters[name] = serverState?.CustomFilter ?? FilterFactory.Create(serverConfig.Tools.Filter);
            _resourceFilters[name] = ResourceFilterFactory.Create(serverConfig.Resources.Filter);
            _promptFilters[name] = PromptFilterFactory.Create(serverConfig.Prompts.Filter);
            _transformers[name] = serverState?.CustomTransformer ?? TransformerFactory.Create(serverConfig.Tools);
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
    public void AddHookPipeline(string serverName, HookPipeline pipeline)
    {
        _hookPipelines[serverName] = pipeline;
    }

    /// <summary>
    /// Gets the hook pipeline for a server.
    /// </summary>
    public HookPipeline? GetHookPipeline(string serverName)
    {
        return _hookPipelines.TryGetValue(serverName, out var pipeline) ? pipeline : null;
    }

    /// <summary>
    /// Lists all tools from all backend servers plus virtual tools.
    /// </summary>
    public ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellationToken)
    {
        return ListToolsCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Lists all tools from all backend servers plus virtual tools.
    /// </summary>
    public async ValueTask<ListToolsResult> ListToolsCoreAsync(CancellationToken cancellationToken)
    {
        ProxyLogger.ListingTools(_logger, _clientManager.Clients.Count);

        var toolsWithServers = new List<ToolWithServer>();

        // Add virtual tools first
        foreach (var virtualTool in _sdkConfiguration.VirtualTools)
        {
            toolsWithServers.Add(new ToolWithServer
            {
                Tool = virtualTool.Tool,
                OriginalName = virtualTool.Tool.Name,
                ServerName = "__virtual__",
                Include = true
            });
        }

        // Add tools from backend servers
        foreach (var (name, clientInfo) in _clientManager.Clients)
        {
            try
            {
                var tools = await clientInfo.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                // Cache the raw tools from the server
                CacheToolsForServer(name, tools);

                var filter = _toolFilters.GetValueOrDefault(name, NoFilter.Instance);
                var transformer = _transformers.GetValueOrDefault(name, NoTransform.Instance);

                foreach (var tool in tools)
                {
                    var shouldInclude = filter.ShouldInclude(tool, name);
                    var transformed = transformer.Transform(tool, name);

                    toolsWithServers.Add(new ToolWithServer
                    {
                        Tool = transformed,
                        OriginalName = tool.Name,
                        ServerName = name,
                        Include = shouldInclude
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list tools from server '{ServerName}'", name);
            }
        }

        // Apply tool interceptors
        IEnumerable<ToolWithServer> interceptedTools = toolsWithServers;
        foreach (var interceptor in _sdkConfiguration.ToolInterceptors)
        {
            interceptedTools = interceptor.InterceptTools(interceptedTools);
        }

        // Filter and collect final tools
        var allTools = interceptedTools
            .Where(t => t.Include)
            .Select(t => t.Tool)
            .ToList();

        ProxyLogger.ToolsListed(_logger, allTools.Count);

        return new ListToolsResult { Tools = allTools };
    }

    /// <summary>
    /// Calls a tool on the appropriate backend server or handles virtual tools.
    /// </summary>
    public ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
    {
        return CallToolCoreAsync(context.Params!, cancellationToken);
    }

    /// <summary>
    /// Calls a tool on the appropriate backend server or handles virtual tools.
    /// </summary>
    public async ValueTask<CallToolResult> CallToolCoreAsync(
        CallToolRequestParams request,
        CancellationToken cancellationToken)
    {
        var toolName = request.Name;

        // Check for virtual tool first
        var virtualTool = _sdkConfiguration.VirtualTools
            .FirstOrDefault(v => string.Equals(v.Tool.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (virtualTool is not null)
        {
            ProxyLogger.CallingTool(_logger, toolName, "__virtual__");
            return await virtualTool.Handler(request, cancellationToken).ConfigureAwait(false);
        }

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

        // Check tool call interceptors
        var callContext = new ToolCallContext
        {
            ToolName = toolName,
            OriginalToolName = toolInfo.OriginalName,
            ServerName = toolInfo.ServerName,
            Request = request,
            AuthenticationResult = GetAuthenticationResult()
        };

        foreach (var interceptor in _sdkConfiguration.ToolCallInterceptors.OrderBy(i => i.Priority))
        {
            var interceptedResult = await interceptor.InterceptAsync(callContext, cancellationToken).ConfigureAwait(false);
            if (interceptedResult is not null)
            {
                return interceptedResult;
            }
        }

        ProxyLogger.CallingTool(_logger, toolInfo.OriginalName, toolInfo.ServerName);

        // Get authentication result from HttpContext if available
        var authResult = GetAuthenticationResult();

        // Create hook context
        var modifiedRequest = new CallToolRequestParams
        {
            Name = toolInfo.OriginalName,
            Arguments = callContext.Request.Arguments // Use potentially modified request
        };

        var hookContext = new HookContext<CallToolRequestParams>
        {
            ServerName = toolInfo.ServerName,
            ToolName = toolInfo.OriginalName,
            Request = modifiedRequest,
            CancellationToken = cancellationToken,
            AuthenticationResult = authResult
        };

        // Execute global pre-invoke hooks
        if (_globalHookPipeline is not null)
        {
            await _globalHookPipeline.ExecutePreInvokeHooksAsync(hookContext).ConfigureAwait(false);
        }

        // Execute server-specific pre-invoke hooks
        var pipeline = GetHookPipeline(toolInfo.ServerName);
        if (pipeline is not null)
        {
            await pipeline.ExecutePreInvokeHooksAsync(hookContext).ConfigureAwait(false);
        }

        // Use the potentially modified cancellation token from hooks
        var effectiveCancellationToken = hookContext.CancellationToken;

        CallToolResult result;
        try
        {
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

    private async Task<CallToolResult> ExecuteToolCallWithRetryAsync(
        ToolInfo toolInfo,
        HookContext<CallToolRequestParams> hookContext,
        HookPipeline? pipeline,
        CancellationToken cancellationToken)
    {
        const int MaxRetryIterations = 10;
        
        for (var iteration = 0; iteration < MaxRetryIterations; iteration++)
        {
            var arguments = hookContext.Request.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value);
            var result = await toolInfo.ClientInfo.Client.CallToolAsync(
                hookContext.Request.Name,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Execute global post-invoke hooks
            if (_globalHookPipeline is not null)
            {
                result = await _globalHookPipeline.ExecutePostInvokeHooksAsync(hookContext, result).ConfigureAwait(false);
            }

            // Execute server-specific post-invoke hooks
            if (pipeline is not null)
            {
                result = await pipeline.ExecutePostInvokeHooksAsync(hookContext, result).ConfigureAwait(false);
            }

            // Check if retry was requested
            if (!hookContext.Items.TryGetValue(RetryHook.RetryRequestedKey, out var retryRequestedObj) ||
                retryRequestedObj is not true)
            {
                return result;
            }

            var delayMs = 0;
            if (hookContext.Items.TryGetValue(RetryHook.RetryDelayMsKey, out var delayObj) && delayObj is int delay)
            {
                delayMs = delay;
            }

            hookContext.Items[RetryHook.RetryRequestedKey] = false;

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }

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

                foreach (var resource in resources)
                {
                    if (filter.ShouldInclude(resource, name))
                    {
                        var transformed = transformer.Transform(resource, name);
                        allResources.Add(transformed);
                    }
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

        var result = await resourceInfo.ClientInfo.Client.ReadResourceAsync(
            resourceInfo.OriginalUri, 
            cancellationToken: cancellationToken).ConfigureAwait(false);

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

                foreach (var prompt in prompts)
                {
                    if (filter.ShouldInclude(prompt, name))
                    {
                        var transformed = transformer.Transform(prompt, name);
                        allPrompts.Add(transformed);
                    }
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
        var result = await promptInfo.ClientInfo.Client.GetPromptAsync(
            promptInfo.OriginalName, 
            arguments, 
            cancellationToken: cancellationToken).ConfigureAwait(false);

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
    /// Subscribes to a resource.
    /// </summary>
    public async ValueTask<EmptyResult> SubscribeToResourceAsync(
        RequestContext<SubscribeRequestParams> context,
        CancellationToken cancellationToken)
    {
        var uri = context.Params!.Uri;
        var resourceInfo = await FindResourceAsync(uri, cancellationToken).ConfigureAwait(false);

        if (resourceInfo is null)
        {
            return new EmptyResult();
        }

        await resourceInfo.ClientInfo.Client.SubscribeToResourceAsync(
            resourceInfo.OriginalUri, 
            cancellationToken).ConfigureAwait(false);

        return new EmptyResult();
    }

    /// <summary>
    /// Unsubscribes from a resource.
    /// </summary>
    public async ValueTask<EmptyResult> UnsubscribeFromResourceAsync(
        RequestContext<UnsubscribeRequestParams> context,
        CancellationToken cancellationToken)
    {
        var uri = context.Params!.Uri;
        var resourceInfo = await FindResourceAsync(uri, cancellationToken).ConfigureAwait(false);

        if (resourceInfo is null)
        {
            return new EmptyResult();
        }

        await resourceInfo.ClientInfo.Client.UnsubscribeFromResourceAsync(
            resourceInfo.OriginalUri, 
            cancellationToken).ConfigureAwait(false);

        return new EmptyResult();
    }

    private async Task<ToolInfo?> FindToolAsync(string toolName, CancellationToken cancellationToken)
    {
        var cachedTool = _toolCache.GetTool(toolName);
        if (cachedTool is not null)
        {
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
                var cachedTools = _toolCache.GetToolsForServer(serverName);
                IList<Tool> tools;

                if (cachedTools is not null)
                {
                    tools = cachedTools;
                }
                else
                {
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
    }

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

    /// <summary>
    /// Invalidates the tool cache for a specific server.
    /// </summary>
    public void InvalidateToolCache(string serverName)
    {
        _toolCache.InvalidateServer(serverName);
    }

    /// <summary>
    /// Invalidates all tool caches.
    /// </summary>
    public void InvalidateAllToolCaches()
    {
        _toolCache.InvalidateAll();
    }
}
