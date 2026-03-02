using McpProxy.Abstractions;
using McpProxy.Core.Configuration;
using McpProxy.Core.Filtering;
using McpProxy.Core.Hooks;
using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace McpProxy.Core.Proxy;

/// <summary>
/// A proxy that exposes a single backend MCP server.
/// Used for PerServer routing mode where each backend gets its own endpoint.
/// </summary>
public sealed class SingleServerProxy
{
    private readonly ILogger<SingleServerProxy> _logger;
    private readonly McpClientManager _clientManager;
    private readonly string _serverName;
    private readonly ServerConfiguration _serverConfig;
    private readonly IToolFilter _filter;
    private readonly IToolTransformer _transformer;
    private readonly ToolPrefixer? _prefixer;
    private HookPipeline? _hookPipeline;

    /// <summary>
    /// Gets the server name this proxy is bound to.
    /// </summary>
    public string ServerName => _serverName;

    /// <summary>
    /// Gets the configured route for this server.
    /// </summary>
    public string Route => _serverConfig.Route ?? $"/{_serverName}";

    /// <summary>
    /// Initializes a new instance of <see cref="SingleServerProxy"/>.
    /// </summary>
    public SingleServerProxy(
        ILogger<SingleServerProxy> logger,
        McpClientManager clientManager,
        string serverName,
        ServerConfiguration serverConfig)
    {
        _logger = logger;
        _clientManager = clientManager;
        _serverName = serverName;
        _serverConfig = serverConfig;

        _filter = FilterFactory.Create(serverConfig.Tools.Filter);
        _transformer = TransformerFactory.Create(serverConfig.Tools);
        _prefixer = !string.IsNullOrEmpty(serverConfig.Tools.Prefix)
            ? new ToolPrefixer(serverConfig.Tools.Prefix, serverConfig.Tools.PrefixSeparator)
            : null;
    }

    /// <summary>
    /// Sets the hook pipeline for this server.
    /// </summary>
    public void SetHookPipeline(HookPipeline pipeline)
    {
        _hookPipeline = pipeline;
    }

    /// <summary>
    /// Lists all tools from this backend server.
    /// </summary>
    public async ValueTask<ListToolsResult> ListToolsAsync(
        CancellationToken cancellationToken = default)
    {
        var clientInfo = GetClientInfo();
        if (clientInfo is null)
        {
            return new ListToolsResult { Tools = [] };
        }

        ProxyLogger.ListingToolsFromServer(_logger, _serverName);

        var allTools = new List<Tool>();

        try
        {
            var tools = await clientInfo.Client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var tool in tools)
            {
                if (_filter.ShouldInclude(tool, _serverName))
                {
                    var transformed = _transformer.Transform(tool, _serverName);
                    allTools.Add(transformed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list tools from server '{ServerName}'", _serverName);
        }

        ProxyLogger.ToolsListed(_logger, allTools.Count);

        return new ListToolsResult { Tools = allTools };
    }

    /// <summary>
    /// Calls a tool on this backend server.
    /// </summary>
    public async ValueTask<CallToolResult> CallToolAsync(
        CallToolRequestParams request,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = GetClientInfo();
        if (clientInfo is null)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Server '{_serverName}' not available" }],
                IsError = true
            };
        }

        var toolName = request.Name;
        var originalName = toolName;

        // Remove prefix if present
        if (_prefixer is not null && _prefixer.HasPrefix(toolName))
        {
            originalName = _prefixer.RemovePrefix(toolName);
        }

        ProxyLogger.CallingTool(_logger, originalName, _serverName);

        // Create hook context
        var modifiedRequest = new CallToolRequestParams
        {
            Name = originalName,
            Arguments = request.Arguments
        };

        var hookContext = new HookContext<CallToolRequestParams>
        {
            ServerName = _serverName,
            ToolName = originalName,
            Request = modifiedRequest,
            CancellationToken = cancellationToken
        };

        // Execute pre-invoke hooks
        if (_hookPipeline is not null)
        {
            await _hookPipeline.ExecutePreInvokeHooksAsync(hookContext).ConfigureAwait(false);
        }

        try
        {
            var arguments = hookContext.Request.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value);
            var result = await clientInfo.Client.CallToolAsync(
                hookContext.Request.Name,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Execute post-invoke hooks
            if (_hookPipeline is not null)
            {
                result = await _hookPipeline.ExecutePostInvokeHooksAsync(hookContext, result).ConfigureAwait(false);
            }

            ProxyLogger.ToolCallCompleted(_logger, originalName);
            return result;
        }
        catch (Exception ex)
        {
            ProxyLogger.ToolCallFailed(_logger, originalName, ex);
            throw;
        }
    }

    /// <summary>
    /// Lists all resources from this backend server.
    /// </summary>
    public async ValueTask<ListResourcesResult> ListResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var clientInfo = GetClientInfo();
        if (clientInfo is null)
        {
            return new ListResourcesResult { Resources = [] };
        }

        ProxyLogger.ListingResourcesFromServer(_logger, _serverName);

        var allResources = new List<Resource>();

        try
        {
            var resources = await clientInfo.Client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            allResources.AddRange(resources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list resources from server '{ServerName}'", _serverName);
        }

        ProxyLogger.ResourcesListed(_logger, allResources.Count);

        return new ListResourcesResult { Resources = allResources };
    }

    /// <summary>
    /// Reads a resource from this backend server.
    /// </summary>
    public async ValueTask<ReadResourceResult> ReadResourceAsync(
        ReadResourceRequestParams request,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = GetClientInfo();
        if (clientInfo is null)
        {
            var uri = request.Uri;
            return new ReadResourceResult
            {
                Contents = [new TextResourceContents { Uri = uri, Text = $"Server '{_serverName}' not available" }]
            };
        }

        var resourceUri = request.Uri;
        ProxyLogger.ReadingResource(_logger, resourceUri, _serverName);

        var result = await clientInfo.Client.ReadResourceAsync(resourceUri, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ReadResourceResult { Contents = [.. result.Contents] };
    }

    /// <summary>
    /// Lists all prompts from this backend server.
    /// </summary>
    public async ValueTask<ListPromptsResult> ListPromptsAsync(
        CancellationToken cancellationToken = default)
    {
        var clientInfo = GetClientInfo();
        if (clientInfo is null)
        {
            return new ListPromptsResult { Prompts = [] };
        }

        ProxyLogger.ListingPromptsFromServer(_logger, _serverName);

        var allPrompts = new List<Prompt>();

        try
        {
            var prompts = await clientInfo.Client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            allPrompts.AddRange(prompts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list prompts from server '{ServerName}'", _serverName);
        }

        ProxyLogger.PromptsListed(_logger, allPrompts.Count);

        return new ListPromptsResult { Prompts = allPrompts };
    }

    /// <summary>
    /// Gets a prompt from this backend server.
    /// </summary>
    public async ValueTask<GetPromptResult> GetPromptAsync(
        GetPromptRequestParams request,
        CancellationToken cancellationToken = default)
    {
        var clientInfo = GetClientInfo();
        if (clientInfo is null)
        {
            return new GetPromptResult
            {
                Messages = [new PromptMessage { Role = Role.Assistant, Content = new TextContentBlock { Text = $"Server '{_serverName}' not available" } }]
            };
        }

        var name = request.Name;
        ProxyLogger.GettingPrompt(_logger, name, _serverName);

        var arguments = request.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value);
        var result = await clientInfo.Client.GetPromptAsync(name, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);

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

    private McpClientInfo? GetClientInfo()
    {
        return _clientManager.Clients.TryGetValue(_serverName, out var clientInfo) ? clientInfo : null;
    }
}
