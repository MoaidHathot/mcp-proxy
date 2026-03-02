using Microsoft.Extensions.Logging;

namespace McpProxy.Core.Logging;

/// <summary>
/// Source-generated logging methods for MCP Proxy.
/// </summary>
public static partial class ProxyLogger
{
    // === Server Lifecycle ===

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP Proxy starting with {TransportType} transport")]
    public static partial void ProxyStarting(ILogger logger, string transportType);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP Proxy started and listening on {Url}")]
    public static partial void ProxyStarted(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP Proxy stopped")]
    public static partial void ProxyStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded configuration from {ConfigPath}")]
    public static partial void ConfigurationLoaded(ILogger logger, string configPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load configuration from {ConfigPath}")]
    public static partial void ConfigurationLoadFailed(ILogger logger, string configPath, Exception exception);

    // === Backend Client Management ===

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to backend server '{ServerName}' ({TransportType})")]
    public static partial void ConnectingToBackend(ILogger logger, string serverName, string transportType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to backend server '{ServerName}'")]
    public static partial void ConnectedToBackend(ILogger logger, string serverName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to connect to backend server '{ServerName}'")]
    public static partial void BackendConnectionFailed(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend server '{ServerName}' disconnected")]
    public static partial void BackendDisconnected(ILogger logger, string serverName);

    // === Tool Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing tools from {ServerCount} backend servers")]
    public static partial void ListingTools(ILogger logger, int serverCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {ToolCount} tools across all servers")]
    public static partial void ToolsListed(ILogger logger, int toolCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling tool '{ToolName}' on server '{ServerName}'")]
    public static partial void CallingTool(ILogger logger, string toolName, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool '{ToolName}' completed successfully")]
    public static partial void ToolCallCompleted(ILogger logger, string toolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool '{ToolName}' failed")]
    public static partial void ToolCallFailed(ILogger logger, string toolName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool '{ToolName}' not found on any server")]
    public static partial void ToolNotFound(ILogger logger, string toolName);

    // === Resource Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing resources from {ServerCount} backend servers")]
    public static partial void ListingResources(ILogger logger, int serverCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {ResourceCount} resources across all servers")]
    public static partial void ResourcesListed(ILogger logger, int resourceCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reading resource '{ResourceUri}' from server '{ServerName}'")]
    public static partial void ReadingResource(ILogger logger, string resourceUri, string serverName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resource '{ResourceUri}' not found on any server")]
    public static partial void ResourceNotFound(ILogger logger, string resourceUri);

    // === Prompt Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing prompts from {ServerCount} backend servers")]
    public static partial void ListingPrompts(ILogger logger, int serverCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {PromptCount} prompts across all servers")]
    public static partial void PromptsListed(ILogger logger, int promptCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting prompt '{PromptName}' from server '{ServerName}'")]
    public static partial void GettingPrompt(ILogger logger, string promptName, string serverName);

    // === Hook Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing {HookCount} pre-invoke hooks for tool '{ToolName}'")]
    public static partial void ExecutingPreInvokeHooks(ILogger logger, int hookCount, string toolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing {HookCount} post-invoke hooks for tool '{ToolName}'")]
    public static partial void ExecutingPostInvokeHooks(ILogger logger, int hookCount, string toolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hook '{HookType}' failed for tool '{ToolName}'")]
    public static partial void HookFailed(ILogger logger, string hookType, string toolName, Exception exception);

    // === Filter Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Filtered {FilteredCount} tools from server '{ServerName}' (mode: {FilterMode})")]
    public static partial void ToolsFiltered(ILogger logger, int filteredCount, string serverName, string filterMode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool '{ToolName}' prefixed to '{PrefixedName}'")]
    public static partial void ToolPrefixed(ILogger logger, string toolName, string prefixedName);

    // === Authentication ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Authenticating request with scheme '{Scheme}'")]
    public static partial void Authenticating(ILogger logger, string scheme);

    [LoggerMessage(Level = LogLevel.Information, Message = "Authentication successful for principal '{PrincipalId}'")]
    public static partial void AuthenticationSucceeded(ILogger logger, string principalId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authentication failed: {Reason}")]
    public static partial void AuthenticationFailed(ILogger logger, string reason);

    // === Routing ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Routing request to endpoint '{Endpoint}'")]
    public static partial void RoutingRequest(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request routed to server '{ServerName}'")]
    public static partial void RequestRouted(ILogger logger, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client handlers configured for sampling, elicitation, and roots")]
    public static partial void ClientHandlersConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered endpoint for server '{ServerName}' at route '{Route}'")]
    public static partial void RegisteredServerEndpoint(ILogger logger, string serverName, string route);

    // === Single Server Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing tools from server '{ServerName}'")]
    public static partial void ListingToolsFromServer(ILogger logger, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing resources from server '{ServerName}'")]
    public static partial void ListingResourcesFromServer(ILogger logger, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listing prompts from server '{ServerName}'")]
    public static partial void ListingPromptsFromServer(ILogger logger, string serverName);

    // === Sampling Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sampling not available: {Reason}")]
    public static partial void SamplingNotAvailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Forwarding sampling request with {MessageCount} messages to client")]
    public static partial void ForwardingSamplingRequest(ILogger logger, int messageCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sampling completed successfully")]
    public static partial void SamplingCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sampling failed")]
    public static partial void SamplingFailed(ILogger logger, Exception exception);

    // === Elicitation Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Elicitation not available: {Reason}")]
    public static partial void ElicitationNotAvailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Forwarding elicitation request: {Message}")]
    public static partial void ForwardingElicitationRequest(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Elicitation completed with action: {Action}")]
    public static partial void ElicitationCompleted(ILogger logger, string action);

    [LoggerMessage(Level = LogLevel.Error, Message = "Elicitation failed")]
    public static partial void ElicitationFailed(ILogger logger, Exception exception);

    // === Roots Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Roots not available: {Reason}")]
    public static partial void RootsNotAvailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Forwarding roots request to client")]
    public static partial void ForwardingRootsRequest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Roots completed with {RootCount} roots")]
    public static partial void RootsCompleted(ILogger logger, int rootCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Roots request failed")]
    public static partial void RootsFailed(ILogger logger, Exception exception);
}
