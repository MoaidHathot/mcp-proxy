using McpProxy.Core.Logging;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpProxy.Core.Proxy;

/// <summary>
/// Provides client handlers for the proxy that forward requests from backend servers to connected clients.
/// </summary>
/// <remarks>
/// <para>
/// When backend MCP servers request sampling, elicitation, or roots from the proxy (as their client),
/// these handlers forward those requests to the proxy's own connected clients.
/// </para>
/// <para>
/// This enables a transparent proxy where advanced MCP features like LLM sampling and user input
/// elicitation can flow through the proxy to the actual client.
/// </para>
/// </remarks>
public sealed class ProxyClientHandlers
{
    private readonly ILogger<ProxyClientHandlers> _logger;
    private McpServer? _mcpServer;

    /// <summary>
    /// Initializes a new instance of <see cref="ProxyClientHandlers"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ProxyClientHandlers(ILogger<ProxyClientHandlers> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the MCP server instance that will be used to forward requests to clients.
    /// </summary>
    /// <param name="mcpServer">The MCP server instance.</param>
    public void SetMcpServer(McpServer mcpServer)
    {
        _mcpServer = mcpServer;
    }

    /// <summary>
    /// Gets whether the proxy has a connected client that supports sampling.
    /// </summary>
    public bool HasSamplingSupport => _mcpServer?.ClientCapabilities?.Sampling is not null;

    /// <summary>
    /// Gets whether the proxy has a connected client that supports elicitation.
    /// </summary>
    public bool HasElicitationSupport => _mcpServer?.ClientCapabilities?.Elicitation is not null;

    /// <summary>
    /// Gets whether the proxy has a connected client that supports roots.
    /// </summary>
    public bool HasRootsSupport => _mcpServer?.ClientCapabilities?.Roots is not null;

    /// <summary>
    /// Handles sampling requests from backend servers by forwarding to the proxy's client.
    /// </summary>
    /// <param name="requestParams">The sampling request parameters.</param>
    /// <param name="progress">Progress reporter for streaming updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sampling result.</returns>
    public async ValueTask<CreateMessageResult> HandleSamplingAsync(
        CreateMessageRequestParams? requestParams,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        if (_mcpServer is null)
        {
            ProxyLogger.SamplingNotAvailable(_logger, "MCP server not initialized");
            return CreateErrorSamplingResult("Proxy server not initialized");
        }

        if (!HasSamplingSupport)
        {
            ProxyLogger.SamplingNotAvailable(_logger, "Client does not support sampling");
            return CreateErrorSamplingResult("Connected client does not support sampling");
        }

        if (requestParams is null)
        {
            ProxyLogger.SamplingNotAvailable(_logger, "Request parameters are null");
            return CreateErrorSamplingResult("Invalid sampling request");
        }

        try
        {
            ProxyLogger.ForwardingSamplingRequest(_logger, requestParams.Messages?.Count ?? 0);
            var result = await _mcpServer.SampleAsync(requestParams, cancellationToken).ConfigureAwait(false);
            ProxyLogger.SamplingCompleted(_logger);
            return result;
        }
        catch (Exception ex)
        {
            ProxyLogger.SamplingFailed(_logger, ex);
            return CreateErrorSamplingResult($"Sampling failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles elicitation requests from backend servers by forwarding to the proxy's client.
    /// </summary>
    /// <param name="requestParams">The elicitation request parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The elicitation result.</returns>
    public async ValueTask<ElicitResult> HandleElicitationAsync(
        ElicitRequestParams? requestParams,
        CancellationToken cancellationToken)
    {
        if (_mcpServer is null)
        {
            ProxyLogger.ElicitationNotAvailable(_logger, "MCP server not initialized");
            return CreateDeclinedElicitResult("Proxy server not initialized");
        }

        if (!HasElicitationSupport)
        {
            ProxyLogger.ElicitationNotAvailable(_logger, "Client does not support elicitation");
            return CreateDeclinedElicitResult("Connected client does not support elicitation");
        }

        if (requestParams is null)
        {
            ProxyLogger.ElicitationNotAvailable(_logger, "Request parameters are null");
            return CreateDeclinedElicitResult("Invalid elicitation request");
        }

        try
        {
            ProxyLogger.ForwardingElicitationRequest(_logger, requestParams.Message ?? "");
            var result = await _mcpServer.ElicitAsync(requestParams, cancellationToken).ConfigureAwait(false);
            ProxyLogger.ElicitationCompleted(_logger, result.Action);
            return result;
        }
        catch (Exception ex)
        {
            ProxyLogger.ElicitationFailed(_logger, ex);
            return CreateDeclinedElicitResult($"Elicitation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles roots requests from backend servers by forwarding to the proxy's client.
    /// </summary>
    /// <param name="requestParams">The roots request parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The roots result.</returns>
    public async ValueTask<ListRootsResult> HandleRootsAsync(
        ListRootsRequestParams? requestParams,
        CancellationToken cancellationToken)
    {
        if (_mcpServer is null)
        {
            ProxyLogger.RootsNotAvailable(_logger, "MCP server not initialized");
            return new ListRootsResult { Roots = [] };
        }

        if (!HasRootsSupport)
        {
            ProxyLogger.RootsNotAvailable(_logger, "Client does not support roots");
            return new ListRootsResult { Roots = [] };
        }

        try
        {
            ProxyLogger.ForwardingRootsRequest(_logger);
            var result = await _mcpServer.RequestRootsAsync(requestParams ?? new ListRootsRequestParams(), cancellationToken).ConfigureAwait(false);
            ProxyLogger.RootsCompleted(_logger, result.Roots?.Count ?? 0);
            return result;
        }
        catch (Exception ex)
        {
            ProxyLogger.RootsFailed(_logger, ex);
            return new ListRootsResult { Roots = [] };
        }
    }

    private static CreateMessageResult CreateErrorSamplingResult(string errorMessage)
    {
        return new CreateMessageResult
        {
            Role = Role.Assistant,
            Content = [new TextContentBlock { Text = errorMessage }],
            Model = "error",
            StopReason = "error"
        };
    }

    private static ElicitResult CreateDeclinedElicitResult(string reason)
    {
        return new ElicitResult
        {
            Action = "decline"
        };
    }
}
