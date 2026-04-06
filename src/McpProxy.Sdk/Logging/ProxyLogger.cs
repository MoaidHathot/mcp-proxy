using Microsoft.Extensions.Logging;

namespace McpProxy.Sdk.Logging;

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Backend server '{ServerName}' uses ForwardAuthorization; connection deferred until first request")]
    public static partial void BackendConnectionDeferred(ILogger logger, string serverName);

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Filtered {FilteredCount} resources from server '{ServerName}'")]
    public static partial void ResourcesFiltered(ILogger logger, int filteredCount, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Filtered {FilteredCount} prompts from server '{ServerName}'")]
    public static partial void PromptsFiltered(ILogger logger, int filteredCount, string serverName);

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Configured {Count} experimental capabilities for client")]
    public static partial void ExperimentalCapabilitiesConfigured(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Configured {Count} experimental capabilities for server")]
    public static partial void ServerExperimentalCapabilitiesConfigured(ILogger logger, int count);

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

    // === Cache Operations ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool cache hit for '{ToolName}' from server '{ServerName}'")]
    public static partial void ToolCacheHit(ILogger logger, string toolName, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool cache miss for '{ToolName}'")]
    public static partial void ToolCacheMiss(ILogger logger, string toolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cached {ToolCount} tools for server '{ServerName}' (TTL: {TtlSeconds}s)")]
    public static partial void ToolsCached(ILogger logger, int toolCount, string serverName, int ttlSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalidated tool cache for server '{ServerName}'")]
    public static partial void ToolCacheInvalidated(ILogger logger, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalidated all tool caches")]
    public static partial void AllToolCachesInvalidated(ILogger logger);

    // === Notification Forwarding ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Forwarding notification '{Method}' to client")]
    public static partial void ForwardingNotification(ILogger logger, string method);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification '{Method}' forwarded successfully")]
    public static partial void NotificationForwarded(ILogger logger, string method);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification forwarding skipped for '{Method}': {Reason}")]
    public static partial void NotificationForwardingSkipped(ILogger logger, string method, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to forward notification '{Method}'")]
    public static partial void NotificationForwardingFailed(ILogger logger, string method, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received notification '{Method}' from server '{ServerName}'")]
    public static partial void ReceivedNotification(ILogger logger, string method, string serverName);

    // === Progress Notification Forwarding ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Forwarding progress notification (token: {ProgressToken}, progress: {Progress}, total: {Total})")]
    public static partial void ForwardingProgressNotification(ILogger logger, string progressToken, double progress, double? total);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Progress notification forwarded (token: {ProgressToken})")]
    public static partial void ProgressNotificationForwarded(ILogger logger, string progressToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Progress notification skipped (token: {ProgressToken}): {Reason}")]
    public static partial void ProgressNotificationSkipped(ILogger logger, string progressToken, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to forward progress notification (token: {ProgressToken})")]
    public static partial void ProgressNotificationFailed(ILogger logger, string progressToken, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received progress notification from server '{ServerName}'")]
    public static partial void ReceivedProgressNotification(ILogger logger, string serverName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse progress notification from server '{ServerName}'")]
    public static partial void ProgressNotificationParsingFailed(ILogger logger, string serverName, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification handlers registered for server '{ServerName}'")]
    public static partial void NotificationHandlersRegistered(ILogger logger, string serverName);

    // === Resource Subscription Operations ===

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribing to resource '{ResourceUri}'")]
    public static partial void SubscribingToResource(ILogger logger, string resourceUri);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribed to resource '{ResourceUri}' on server '{ServerName}'")]
    public static partial void SubscribedToResource(ILogger logger, string resourceUri, string serverName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resource '{ResourceUri}' not found for subscription")]
    public static partial void ResourceNotFoundForSubscription(ILogger logger, string resourceUri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend server '{ServerName}' not found for subscription to '{ResourceUri}'")]
    public static partial void BackendNotFoundForSubscription(ILogger logger, string serverName, string resourceUri);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to subscribe to resource '{ResourceUri}' on server '{ServerName}'")]
    public static partial void ResourceSubscriptionFailed(ILogger logger, string resourceUri, string serverName, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unsubscribing from resource '{ResourceUri}'")]
    public static partial void UnsubscribingFromResource(ILogger logger, string resourceUri);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unsubscribed from resource '{ResourceUri}' on server '{ServerName}'")]
    public static partial void UnsubscribedFromResource(ILogger logger, string resourceUri, string serverName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No subscription found to unsubscribe from resource '{ResourceUri}'")]
    public static partial void NoSubscriptionToUnsubscribe(ILogger logger, string resourceUri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend server '{ServerName}' not found for unsubscription from '{ResourceUri}'")]
    public static partial void BackendNotFoundForUnsubscription(ILogger logger, string serverName, string resourceUri);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to unsubscribe from resource '{ResourceUri}' on server '{ServerName}'")]
    public static partial void ResourceUnsubscriptionFailed(ILogger logger, string resourceUri, string serverName, Exception exception);

    // === Token Acquisition ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Acquiring token using {AuthType} for {Authority}")]
    public static partial void AcquiringToken(ILogger logger, string authType, string authority);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Token acquired successfully using {AuthType}")]
    public static partial void TokenAcquired(ILogger logger, string authType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to acquire token using {AuthType}: {ErrorMessage}")]
    public static partial void TokenAcquisitionFailed(ILogger logger, string authType, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to add authorization header for backend request")]
    public static partial void BackendAuthorizationFailed(ILogger logger, Exception exception);

    // === Hook Operations (Built-in Hooks) ===

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded for key '{Key}' (limit: {Limit} requests per {WindowSeconds}s)")]
    public static partial void RateLimitExceeded(ILogger logger, string key, int limit, int windowSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rate limit check passed for key '{Key}' ({CurrentCount}/{Limit})")]
    public static partial void RateLimitChecked(ILogger logger, string key, int currentCount, int limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authorization denied for tool '{ToolName}' (principal: {PrincipalId}): {Reason}")]
    public static partial void AuthorizationDenied(ILogger logger, string toolName, string? principalId, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Authorization granted for tool '{ToolName}' (principal: {PrincipalId})")]
    public static partial void AuthorizationGranted(ILogger logger, string toolName, string? principalId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Timeout configured for tool '{ToolName}': {TimeoutSeconds}s")]
    public static partial void TimeoutConfigured(ILogger logger, string toolName, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry attempt {Attempt}/{MaxRetries} for tool '{ToolName}' after {DelayMs}ms delay")]
    public static partial void RetryAttempt(ILogger logger, int attempt, int maxRetries, string toolName, int delayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retry exhausted for tool '{ToolName}' after {MaxRetries} attempts")]
    public static partial void RetryExhausted(ILogger logger, string toolName, int maxRetries);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retry requested for tool '{ToolName}': error matches pattern '{Pattern}'")]
    public static partial void RetryRequested(ILogger logger, string toolName, string pattern);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit: {Action} tool '{ToolName}' on server '{ServerName}' by principal '{PrincipalId}'")]
    public static partial void AuditEntry(ILogger logger, string action, string toolName, string serverName, string? principalId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Audit: Tool '{ToolName}' completed in {DurationMs}ms with status '{Status}'")]
    public static partial void AuditCompletion(ILogger logger, string toolName, long durationMs, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Content filter triggered for tool '{ToolName}': pattern '{PatternName}' matched (mode: {Mode})")]
    public static partial void ContentFilterTriggered(ILogger logger, string toolName, string patternName, string mode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Content blocked for tool '{ToolName}': {Reason}")]
    public static partial void ContentBlocked(ILogger logger, string toolName, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Metrics recorded for tool '{ToolName}': duration={DurationMs}ms, success={Success}")]
    public static partial void MetricsRecorded(ILogger logger, string toolName, double durationMs, bool success);

    // === Hook Tracing ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hook trace started for tool '{ToolName}' on server '{ServerName}'")]
    public static partial void HookTraceStarted(ILogger logger, string toolName, string serverName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hook '{HookName}' ({HookType}, priority {Priority}) executing")]
    public static partial void HookExecuting(ILogger logger, string hookName, string hookType, int priority);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hook '{HookName}' completed in {DurationMs:F2}ms")]
    public static partial void HookCompleted(ILogger logger, string hookName, double durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hook '{HookName}' failed: {ErrorType} - {ErrorMessage}")]
    public static partial void HookTraceFailed(ILogger logger, string hookName, string errorType, string errorMessage);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hook trace completed for '{ToolName}' on '{ServerName}': {TotalHooks} hooks ({CompletedHooks} completed, {FailedHooks} failed) in {TotalDurationMs:F2}ms")]
    public static partial void HookTraceSummary(ILogger logger, string toolName, string serverName, int totalHooks, int completedHooks, int failedHooks, double totalDurationMs);

    // === Request/Response Dumping ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dump written to file: {FilePath}")]
    public static partial void DumpWrittenToFile(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[DUMP {Type}] Server: {ServerName}, Tool: {ToolName}\n{Content}")]
    public static partial void DumpToConsole(ILogger logger, string type, string serverName, string toolName, string content);

    // === Debug Health ===

    [LoggerMessage(Level = LogLevel.Information, Message = "Debug health endpoint enabled at '{Path}' (localhost-only)")]
    public static partial void DebugHealthEndpointEnabled(ILogger logger, string path);

    // === Health Tracking ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Health recorded success for backend '{BackendName}': {ResponseTimeMs:F2}ms")]
    public static partial void HealthRecordedSuccess(ILogger logger, string backendName, double responseTimeMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Health recorded failure for backend '{BackendName}': {ErrorMessage}")]
    public static partial void HealthRecordedFailure(ILogger logger, string backendName, string errorMessage);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backend '{BackendName}' connected")]
    public static partial void HealthBackendConnected(ILogger logger, string backendName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backend '{BackendName}' disconnected")]
    public static partial void HealthBackendDisconnected(ILogger logger, string backendName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Backend '{BackendName}' capabilities recorded: {ToolCount} tools, {PromptCount} prompts, {ResourceCount} resources")]
    public static partial void HealthRecordedCapabilities(ILogger logger, string backendName, int toolCount, int promptCount, int resourceCount);

    // === Forward Authorization ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Forwarding Authorization header to backend")]
    public static partial void ForwardAuthorizationHeaderAdded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No HTTP context available for authorization forwarding (stdio mode?)")]
    public static partial void ForwardAuthorizationNoContext(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Authorization header present but empty, not forwarding")]
    public static partial void ForwardAuthorizationHeaderEmpty(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No Authorization header found in incoming request")]
    public static partial void ForwardAuthorizationHeaderMissing(ILogger logger);

    // === OAuth Metadata Proxy ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Proxied OAuth metadata from {TargetUrl}, status: {StatusCode}")]
    public static partial void OAuthMetadataProxied(ILogger logger, string targetUrl, System.Net.HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch OAuth metadata from {TargetUrl}")]
    public static partial void OAuthMetadataFetchFailed(ILogger logger, string targetUrl, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout fetching OAuth metadata from {TargetUrl}")]
    public static partial void OAuthMetadataTimeout(ILogger logger, string targetUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mapped proxied OAuth metadata endpoints to backend: {BackendUrl}")]
    public static partial void OAuthMetadataProxyMapped(ILogger logger, string backendUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mapped cached proxied OAuth metadata endpoints to backend: {BackendUrl} (cache: {CacheDuration})")]
    public static partial void OAuthMetadataProxyCachedMapped(ILogger logger, string backendUrl, TimeSpan cacheDuration);

    // === OAuth Metadata Probing ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Probing backend {BackendUrl} for OAuth metadata endpoints")]
    public static partial void OAuthMetadataProbeStarting(ILogger logger, string backendUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backend {BackendUrl} supports OAuth: oauth-authorization-server={SupportsOAuth}, openid-configuration={SupportsOpenId}")]
    public static partial void OAuthMetadataProbeSuccess(ILogger logger, string backendUrl, bool supportsOAuth, bool supportsOpenId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Backend {BackendUrl} does not support OAuth metadata endpoints")]
    public static partial void OAuthMetadataProbeNoSupport(ILogger logger, string backendUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found OAuth endpoint {Path} at {TargetUrl}")]
    public static partial void OAuthMetadataProbeEndpointFound(ILogger logger, string path, string targetUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "OAuth endpoint {Path} not found at {TargetUrl} (status: {StatusCode})")]
    public static partial void OAuthMetadataProbeEndpointNotFound(ILogger logger, string path, string targetUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Error probing OAuth endpoint {Path} at {TargetUrl}")]
    public static partial void OAuthMetadataProbeEndpointError(ILogger logger, string path, string targetUrl, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Timeout probing OAuth endpoint {Path} at {TargetUrl}")]
    public static partial void OAuthMetadataProbeEndpointTimeout(ILogger logger, string path, string targetUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-configured OAuth metadata proxy for backend '{ServerName}' ({BackendUrl})")]
    public static partial void OAuthMetadataAutoConfigured(ILogger logger, string serverName, string backendUrl);

    // === RFC 9728 OAuth Protected Resource Metadata ===

    [LoggerMessage(Level = LogLevel.Information, Message = "Backend {BackendUrl} supports RFC 9728 OAuth Protected Resource Metadata: authorization_servers={AuthorizationServers}")]
    public static partial void OAuthProtectedResourceProbeSuccess(ILogger logger, string backendUrl, string authorizationServers);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Probing RFC 9728 protected resource metadata at {TargetUrl}")]
    public static partial void OAuthProtectedResourceProbeStarting(ILogger logger, string targetUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Serving RFC 9728 protected resource metadata for path '{Path}'")]
    public static partial void ServingProtectedResourceMetadata(ILogger logger, string path);

    // === Default Azure Credential ===

    [LoggerMessage(Level = LogLevel.Debug, Message = "Acquired token via DefaultAzureCredential for scope '{Scope}'")]
    public static partial void DefaultAzureCredentialTokenAcquired(ILogger logger, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to acquire token via DefaultAzureCredential")]
    public static partial void DefaultAzureCredentialTokenFailed(ILogger logger, Exception exception);
}
