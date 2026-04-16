using System.Text;
using System.Text.Json;
using Azure.Identity;
using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Proxy;
using McpProxy.Sdk.Sdk;
using McpProxy.Samples.TeamsIntegration;
using McpProxy.Samples.TeamsIntegration.Cache;

// ═══════════════════════════════════════════════════════════════════════════
// Teams Integration Sample
// ═══════════════════════════════════════════════════════════════════════════
// This sample demonstrates how to use MCP-Proxy hooks, interceptors, and
// virtual tools to provide a rich Teams integration layer that includes:
//
// - Automatic caching of Teams data (chats, people, teams, channels)
// - Cache short-circuiting to avoid redundant MCP calls
// - Credential scanning to block messages containing secrets
// - Automatic pagination enforcement
// - Message prefixing for AI-generated content
// - Virtual tools for cache lookup, URL parsing, and validation
//
// Two authentication modes are supported:
//
// 1. FORWARD-AUTH (default): HTTP/SSE mode where the proxy authenticates
//    interactively via the browser. A browser window opens for sign-in and
//    consent, and VS Code connects to the proxy without authentication.
//    Best for: Interactive use where users authenticate themselves.
//
// 2. PROXY-AUTH: stdio mode where the proxy authenticates with Azure AD
//    using app credentials. Clients connect without authentication.
//    Best for: Automated agents, scripts, or simplified client setup.
//
// 3. USER-AUTH: stdio mode where the proxy authenticates as the current user
//    using interactive browser login (impersonating VS Code's client ID).
//    Clients connect without authentication. After first sign-in, tokens
//    are cached and refreshed silently.
//    Best for: Non-OAuth clients (OpenCode, etc.) that need user-delegated access.
//
// ═══════════════════════════════════════════════════════════════════════════

// Parse command-line arguments and environment variables
// Supports both --key=value and --key value formats
static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }

        if (args[i].StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
        {
            return args[i].Split('=', 2)[1];
        }
    }

    return null;
}

var tenantId = Environment.GetEnvironmentVariable("TENANT_ID")
    ?? GetArg(args, "--tenant-id")
    ?? throw new InvalidOperationException(
        "Tenant ID is required. Set TENANT_ID environment variable or pass --tenant-id <your-tenant-id>");

var authMode = Environment.GetEnvironmentVariable("AUTH_MODE")?.ToLowerInvariant()
    ?? GetArg(args, "--auth-mode")?.ToLowerInvariant()
    ?? "forward-auth";

var port = int.TryParse(
    Environment.GetEnvironmentVariable("PORT") ?? GetArg(args, "--port"),
    out var p) ? p : 5101;

var includeVirtualTools = args.Contains("--include-virtual", StringComparer.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("INCLUDE_VIRTUAL"), "true", StringComparison.OrdinalIgnoreCase);

var logToken = args.Contains("--log-token", StringComparer.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("LOG_TOKEN"), "true", StringComparison.OrdinalIgnoreCase);

// Validate auth mode
var authModeType = authMode switch
{
    "forward-auth" => AuthModeType.ForwardAuth,
    "proxy-auth" => AuthModeType.ProxyAuth,
    "user-auth" => AuthModeType.UserAuth,
    _ => throw new InvalidOperationException(
        $"Invalid AUTH_MODE: '{authMode}'. Valid values: 'forward-auth' (default), 'proxy-auth', 'user-auth'")
};

// For proxy-auth mode, we need Azure AD app credentials
string? clientId = null;
string? clientSecret = null;
string[]? scopes = null;

if (authModeType == AuthModeType.ProxyAuth)
{
    clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
        ?? GetArg(args, "--client-id")
        ?? throw new InvalidOperationException(
            "AZURE_CLIENT_ID is required for proxy-auth mode. " +
            "Set the environment variable or pass --client-id <your-app-client-id>");

    // Client secret can use env: prefix for environment variable lookup
    clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET")
        ?? GetArg(args, "--client-secret")
        ?? "env:AZURE_CLIENT_SECRET"; // Default to env lookup

    // Scopes for Teams MCP Server (may need adjustment based on actual API requirements)
    var scopeValue = Environment.GetEnvironmentVariable("AZURE_SCOPES")
        ?? GetArg(args, "--scopes");
    
    scopes = scopeValue?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? ["https://graph.microsoft.com/.default"];
}

// For user-auth mode, we need VS Code's client ID
string? vsCodeClientId = null;

if (authModeType == AuthModeType.UserAuth)
{
    vsCodeClientId = Environment.GetEnvironmentVariable("VSCODE_CLIENT_ID")
        ?? GetArg(args, "--vscode-client-id")
        ?? throw new InvalidOperationException(
            "VSCODE_CLIENT_ID is required for user-auth mode. " +
            "Run with --auth-mode=forward-auth --log-token first to discover VS Code's client ID, " +
            "then pass it via --vscode-client-id or VSCODE_CLIENT_ID environment variable.");

    // Scopes can be provided explicitly or discovered from RFC 9728 metadata
    var scopeValue = Environment.GetEnvironmentVariable("AZURE_SCOPES")
        ?? GetArg(args, "--scopes");

    scopes = scopeValue?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    // If scopes are not provided, they will be discovered from RFC 9728 metadata in RunUserAuthModeAsync
}

var teamsServerUrl = $"https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/mcp_TeamsServer";

// Lazy reference to the service provider, set after Build() completes.
// This allows virtual tool handlers (which run at request time) to resolve DI services.
IServiceProvider? _resolvedServices = null;

// ═══════════════════════════════════════════════════════════════════════════
// Application Configuration
// ═══════════════════════════════════════════════════════════════════════════

switch (authModeType)
{
    case AuthModeType.ProxyAuth:
        // PROXY-AUTH MODE: stdio transport, proxy handles Azure AD authentication
        await RunProxyAuthModeAsync(tenantId, clientId!, clientSecret!, scopes!, teamsServerUrl).ConfigureAwait(false);
        break;
    case AuthModeType.UserAuth:
        // USER-AUTH MODE: stdio transport, proxy authenticates as current user
        await RunUserAuthModeAsync(tenantId, vsCodeClientId!, scopes, teamsServerUrl).ConfigureAwait(false);
        break;
    default:
        // FORWARD-AUTH MODE: HTTP transport, VS Code handles Azure AD authentication
        await RunForwardAuthModeAsync(tenantId, port, teamsServerUrl).ConfigureAwait(false);
        break;
}

// ═══════════════════════════════════════════════════════════════════════════
// PROXY-AUTH MODE: stdio transport with proxy-managed Azure AD authentication
// ═══════════════════════════════════════════════════════════════════════════
async Task RunProxyAuthModeAsync(string tenantId, string clientId, string clientSecret, string[] scopes, string teamsServerUrl)
{
    Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.Error.WriteLine("Teams Integration Sample - PROXY-AUTH MODE (stdio)");
    Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Tenant ID: {tenantId}");
    Console.Error.WriteLine($"Client ID: {clientId}");
    Console.Error.WriteLine($"Scopes: {string.Join(", ", scopes)}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("The proxy will authenticate with Azure AD using app credentials.");
    Console.Error.WriteLine("Clients connect via stdio without authentication.");
    Console.Error.WriteLine();

    var builder = Host.CreateApplicationBuilder(args);
    
    // Configure logging to stderr (stdout is used for MCP messages)
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Add Teams integration services (creates the shared cache instance)
    var teamsContext = builder.Services.AddTeamsIntegration(ConfigureTeamsIntegration);

    // Configure MCP Proxy with Azure AD Client Credentials auth
    builder.Services.AddMcpProxy(proxy =>
    {
        proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
            "MCP Proxy with Teams caching, credential scanning, and virtual tools. " +
            "Running in proxy-auth mode - no client authentication required.");

        // Connect to Teams MCP Server with proxy-managed Azure AD authentication.
        // Uses HTTP (Streamable HTTP) transport — the Teams backend uses POST-based
        // JSON-RPC, not legacy SSE.
        proxy.AddHttpServer("teams", teamsServerUrl)
            .WithTitle("Microsoft Teams")
            .WithDescription("Microsoft Teams MCP Server for chat, messaging, and collaboration")
            .WithToolPrefix("teams")
            .WithBackendAuth(BackendAuthType.AzureAdClientCredentials, azureAd =>
            {
                azureAd.TenantId = tenantId;
                azureAd.ClientId = clientId;
                azureAd.ClientSecret = clientSecret;
                azureAd.Scopes = scopes;
            })
            .Build();

        AddStatusTool(proxy, tenantId, "stdio (proxy-auth)", "Proxy-managed Azure AD authentication");

        // Apply Teams integration hooks, interceptors, and virtual tools
        // Uses the same cache instance as DI (via teamsContext)
        proxy.WithTeamsIntegration(teamsContext);
    });

    // Configure MCP Server with stdio transport (no auth required from clients)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithSdkProxyHandlers();

    var app = builder.Build();
    _resolvedServices = app.Services;

    // Initialize cache
    await InitializeCacheAsync(app.Services).ConfigureAwait(false);

    // Initialize MCP Proxy
    Console.Error.WriteLine("Initializing MCP Proxy with Teams integration...");
    await app.InitializeMcpProxyAsync().ConfigureAwait(false);
    Console.Error.WriteLine("MCP Proxy initialized!");
    Console.Error.WriteLine();

    PrintFeatures("stdio (proxy-auth)", AuthModeType.ProxyAuth);

    await app.RunAsync().ConfigureAwait(false);
}

// ═══════════════════════════════════════════════════════════════════════════
// USER-AUTH MODE: stdio transport with user-delegated authentication
// ═══════════════════════════════════════════════════════════════════════════
// In this mode the proxy authenticates as the current user by performing an
// interactive browser login using VS Code's client ID. This produces a
// user-delegated token identical to what VS Code would obtain, allowing
// non-OAuth clients (OpenCode, etc.) to access the Teams MCP Server.
//
// On first run a browser window opens for sign-in. Subsequent runs use the
// cached refresh token silently (typically valid for ~90 days).
// ═══════════════════════════════════════════════════════════════════════════
async Task RunUserAuthModeAsync(string tenantId, string vsCodeClientId, string[]? scopes, string teamsServerUrl)
{
    Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.Error.WriteLine("Teams Integration Sample - USER-AUTH MODE (stdio)");
    Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Tenant ID:         {tenantId}");
    Console.Error.WriteLine($"VS Code Client ID: {vsCodeClientId}");
    Console.Error.WriteLine();

    // ── Step 1: Discover scopes from RFC 9728 metadata if not provided ──
    if (scopes is null || scopes.Length == 0)
    {
        Console.Error.WriteLine("No --scopes provided. Discovering from RFC 9728 protected resource metadata...");
        var audience = await DiscoverAudienceFromMetadataAsync(teamsServerUrl).ConfigureAwait(false);

        if (audience is not null)
        {
            // Use .default scope to request all pre-consented permissions for the resource
            scopes = [$"{audience}/.default"];
            Console.Error.WriteLine($"Discovered audience: {audience}");
            Console.Error.WriteLine($"Using scopes:        {scopes[0]}");
        }
        else
        {
            throw new InvalidOperationException(
                "Could not discover audience from RFC 9728 metadata. " +
                "Provide scopes explicitly via --scopes or AZURE_SCOPES environment variable.");
        }
    }
    else
    {
        Console.Error.WriteLine($"Scopes: {string.Join(", ", scopes)}");
    }

    Console.Error.WriteLine();

    // ── Step 2: Create credential with persistent token cache ──
    // InteractiveBrowserCredential opens the system browser for sign-in on first use.
    // After authentication, the refresh token is persisted to the OS credential store
    // (Windows Credential Manager / macOS Keychain / Linux libsecret) so subsequent
    // runs are completely silent.
    var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
    {
        ClientId = vsCodeClientId,
        TenantId = tenantId,
        TokenCachePersistenceOptions = new TokenCachePersistenceOptions
        {
            Name = "mcp-proxy-teams"
        },
        RedirectUri = new Uri("http://localhost")
    });

    // ── Step 3: Pre-authenticate before starting the MCP server ──
    // This ensures we have a valid token before accepting client connections.
    // On first run: opens browser. On subsequent runs: silent refresh.
    Console.Error.WriteLine("Authenticating with Azure AD...");
    Console.Error.WriteLine("If this is your first time, a browser window will open for sign-in.");
    Console.Error.WriteLine();

    try
    {
        var tokenContext = new Azure.Core.TokenRequestContext(scopes);
        var token = await credential.GetTokenAsync(tokenContext).ConfigureAwait(false);

        // Decode the token to show who we authenticated as
        var upn = TokenLoggerExtensions.DecodeUpnFromToken(token.Token);
        Console.Error.WriteLine($"Authenticated as: {upn ?? "(unknown)"}");
        Console.Error.WriteLine($"Token expires:    {token.ExpiresOn:yyyy-MM-dd HH:mm:ss K}");
    }
    catch (AuthenticationFailedException ex)
    {
        Console.Error.WriteLine($"Authentication failed: {ex.Message}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Troubleshooting:");
        Console.Error.WriteLine("  1. Ensure the VS Code client ID is correct (use --log-token in forward-auth mode to discover it)");
        Console.Error.WriteLine("  2. Check that your tenant ID is correct");
        Console.Error.WriteLine("  3. Verify that the browser opened and you completed the sign-in");
        throw;
    }

    Console.Error.WriteLine();

    // ── Step 4: Configure the proxy ──
    var builder = Host.CreateApplicationBuilder(args);

    // Configure logging to stderr (stdout is used for MCP messages)
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Add Teams integration services
    var teamsContext = builder.Services.AddTeamsIntegration(ConfigureTeamsIntegration);

    // Configure MCP Proxy with DefaultAzureCredential backend auth + injected credential.
    // The InteractiveBrowserCredential handles token refresh automatically — MSAL uses
    // the cached refresh token to silently acquire new access tokens as they expire.
    builder.Services.AddMcpProxy(proxy =>
    {
        proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
            "MCP Proxy with Teams caching, credential scanning, and virtual tools. " +
            "Running in user-auth mode - authenticated as the current user via interactive browser.");

        proxy.AddHttpServer("teams", teamsServerUrl)
            .WithTitle("Microsoft Teams")
            .WithDescription("Microsoft Teams MCP Server for chat, messaging, and collaboration")
            .WithToolPrefix("teams")
            .WithBackendAuth(BackendAuthType.AzureDefaultCredential, azureAd =>
            {
                azureAd.TenantId = tenantId;
                azureAd.Scopes = scopes;
                azureAd.TokenCredential = credential;
            })
            .Build();

        AddStatusTool(proxy, tenantId, "stdio (user-auth)", "User-delegated (interactive browser)");

        // Apply Teams integration hooks, interceptors, and virtual tools
        proxy.WithTeamsIntegration(teamsContext);
    });

    // Configure MCP Server with stdio transport (no auth required from clients)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithSdkProxyHandlers();

    var app = builder.Build();
    _resolvedServices = app.Services;

    // Initialize cache
    await InitializeCacheAsync(app.Services).ConfigureAwait(false);

    // Initialize MCP Proxy
    Console.Error.WriteLine("Initializing MCP Proxy with Teams integration...");
    await app.InitializeMcpProxyAsync().ConfigureAwait(false);
    Console.Error.WriteLine("MCP Proxy initialized!");
    Console.Error.WriteLine();

    PrintFeatures("stdio (user-auth)", AuthModeType.UserAuth);

    await app.RunAsync().ConfigureAwait(false);
}

// ═══════════════════════════════════════════════════════════════════════════
// RFC 9728 Audience Discovery
// ═══════════════════════════════════════════════════════════════════════════

// Discovers the resource audience from the Teams backend's RFC 9728 OAuth Protected
// Resource Metadata. The well-known URL is constructed per RFC 9728 by inserting
// /.well-known/oauth-protected-resource between the host and path.
static async Task<string?> DiscoverAudienceFromMetadataAsync(string teamsServerUrl)
{
    try
    {
        var uri = new Uri(teamsServerUrl);
        var wellKnownUrl = $"{uri.Scheme}://{uri.Authority}/.well-known/oauth-protected-resource{uri.AbsolutePath}";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var response = await httpClient.GetAsync(wellKnownUrl).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"RFC 9728 probe returned {(int)response.StatusCode} from {wellKnownUrl}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);

        // The "resource" field contains the audience identifier
        if (doc.RootElement.TryGetProperty("resource", out var resource) &&
            resource.ValueKind == JsonValueKind.String)
        {
            return resource.GetString();
        }

        Console.Error.WriteLine("RFC 9728 metadata did not contain a 'resource' field.");
        return null;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to discover audience from RFC 9728 metadata: {ex.Message}");
        return null;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// FORWARD-AUTH MODE: HTTP transport with client-managed authentication
// ═══════════════════════════════════════════════════════════════════════════
// In this mode the proxy is transparent for auth: VS Code (or any MCP client)
// authenticates directly with the Teams MCP backend via OAuth, and the proxy
// simply forwards the Authorization header as-is. No app registration or
// client credentials are needed for the proxy itself.
//
// The core SDK automatically:
//   1. Probes RFC 9728 OAuth Protected Resource Metadata on the backend
//   2. Exposes that metadata to MCP clients so they can obtain tokens
//   3. Forwards the client's Authorization header on every backend call
// ═══════════════════════════════════════════════════════════════════════════
async Task RunForwardAuthModeAsync(string tenantId, int port, string teamsServerUrl)
{
    Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.WriteLine("Teams Integration Sample - FORWARD-AUTH MODE (HTTP)");
    Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.WriteLine();

    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls($"http://localhost:{port}");

    // Configure logging
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Add Teams integration services (creates the shared cache instance)
    var teamsContext = builder.Services.AddTeamsIntegration(ConfigureTeamsIntegration);

    // Configure MCP Proxy with ForwardAuthorization — the proxy is transparent for auth
    builder.Services.AddMcpProxy(proxy =>
    {
        proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
            "MCP Proxy with Teams caching, credential scanning, and virtual tools. " +
            "Running in forward-auth mode - the client authenticates directly with the " +
            "Teams backend and the proxy forwards the Authorization header transparently.");

        // Connect to Teams MCP Server with ForwardAuthorization.
        // Uses HTTP (Streamable HTTP) transport — the Teams backend uses POST-based
        // JSON-RPC, not legacy SSE.
        // The proxy does not authenticate itself — it passes through the client's token.
        // During InitializeMcpProxyAsync(), the SDK probes the backend's RFC 9728 metadata
        // so MCP clients can discover the required OAuth scopes and authorization server.
        proxy.AddHttpServer("teams", teamsServerUrl)
            .WithTitle("Microsoft Teams")
            .WithDescription("Microsoft Teams MCP Server for chat, messaging, and collaboration")
            .WithToolPrefix("teams")
            .WithBackendAuth(BackendAuthType.ForwardAuthorization)
            .Build();

        AddStatusTool(proxy, tenantId, $"http://localhost:{port}/mcp", "ForwardAuthorization (client-managed)");

        // Apply Teams integration hooks, interceptors, and virtual tools
        // Uses the same cache instance as DI (via teamsContext)
        proxy.WithTeamsIntegration(teamsContext);
    });

    // Configure MCP Server with HTTP transport and proxy handlers
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithSdkProxyHandlers();

    var app = builder.Build();
    _resolvedServices = app.Services;

    // Initialize cache
    await InitializeCacheAsync(app.Services).ConfigureAwait(false);

    // Initialize MCP Proxy (probes OAuth metadata on ForwardAuthorization backends)
    Console.WriteLine();
    Console.WriteLine("Initializing MCP Proxy with Teams integration...");
    await app.InitializeMcpProxyAsync().ConfigureAwait(false);
    Console.WriteLine("MCP Proxy initialized!");
    Console.WriteLine();
    Console.WriteLine($"Tenant ID:   {tenantId}");
    Console.WriteLine($"Auth mode:   ForwardAuthorization (proxy is transparent)");
    Console.WriteLine($"Proxy URL:   http://localhost:{port}/mcp");
    if (logToken)
    {
        Console.WriteLine($"Token log:   ENABLED (will log JWT claims from first authenticated request)");
    }
    Console.WriteLine();

    PrintFeatures($"http://localhost:{port}/mcp", AuthModeType.ForwardAuth);

    // Enable OAuth metadata proxy middleware so VS Code can discover how to authenticate.
    // This serves the backend's RFC 9728 OAuth Protected Resource Metadata on the proxy's
    // own URL, allowing MCP clients to find the authorization server and scopes.
    // Must be called before UseForwardAuthAuthentication() and MapMcp().
    app.UseOAuthMetadataProxy();

    // Enable forward-auth authentication middleware. This requires a Bearer token on
    // incoming requests and returns 401 with RFC 9728 resource_metadata hints if missing.
    // VS Code sees the 401 challenge, discovers the OAuth metadata, and triggers its
    // OAuth flow to obtain a token. The proxy then forwards the token to the backend.
    app.UseForwardAuthAuthentication();

    // When --log-token is passed, log the JWT claims from the first authenticated request.
    // This allows discovering VS Code's client ID, audience, and scopes so they can be
    // used with --auth-mode=user-auth to authenticate without VS Code.
    if (logToken)
    {
        app.UseTokenLogger();
    }

    // Map MCP endpoint
    app.MapMcp("/mcp");

    // Handle shutdown
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() => SaveCacheOnShutdown(app.Services));

    await app.RunAsync().ConfigureAwait(false);
}

// ═══════════════════════════════════════════════════════════════════════════
// Shared Configuration
// ═══════════════════════════════════════════════════════════════════════════

void ConfigureTeamsIntegration(TeamsIntegrationOptions options)
{
    // Configure cache settings
    options.CacheTtl = TimeSpan.FromHours(4);
    options.MaxRecentContacts = 50;

    // Configure hooks
    options.EnableAutoPagination = true;
    options.DefaultPaginationLimit = 20;

    options.EnableCredentialScanning = true;
    options.BlockCredentials = true;

    options.EnableMessagePrefix = false;
    options.MessagePrefix = "[AI]";

    // Configure caching behavior
    options.EnableCachePopulation = true;
    options.EnableCacheShortCircuit = true;
    options.LoadCacheOnStartup = true;
    options.AutoSaveCache = false;

    // Virtual tools are hidden by default — the proxy handles caching, resolution,
    // and validation transparently. Pass --include-virtual to expose them for diagnostics.
    options.RegisterVirtualTools = includeVirtualTools;
}

async Task InitializeCacheAsync(IServiceProvider services)
{
    var cacheService = services.GetService<ITeamsCacheService>();
    if (cacheService is not null)
    {
        Console.Error.WriteLine("Loading Teams cache from disk...");
        await cacheService.LoadAsync().ConfigureAwait(false);
        var status = cacheService.GetStatus();
        Console.Error.WriteLine($"Cache loaded: {status.PersonCount} people, {status.ChatCount} chats, " +
                     $"{status.TeamCount} teams, {status.ChannelCount} channels");
    }
}

void SaveCacheOnShutdown(IServiceProvider services)
{
    var cacheService = services.GetService<ITeamsCacheService>();
    if (cacheService is not null)
    {
        Console.Error.WriteLine("Saving Teams cache to disk...");
        cacheService.SaveAsync().GetAwaiter().GetResult();
        Console.Error.WriteLine("Cache saved.");
    }
}

void AddStatusTool(McpProxy.Abstractions.IMcpProxyBuilder proxy, string tenantId, string endpoint, string authMethod)
{
    proxy.AddTool(
        name: "sample_status",
        description: "Get the status of the Teams integration sample, including backend connection state",
        handler: (request, ct) =>
        {
            // Resolve connection state from the client manager (available at request time)
            var clientManager = _resolvedServices?.GetService<McpClientManager>();
            var connectedBackends = clientManager?.Clients.Keys.ToList() ?? [];
            var deferredBackends = clientManager?.DeferredClientNames.ToList() ?? [];

            var connectionState = deferredBackends.Count == 0
                ? "all_connected"
                : connectedBackends.Count > 0
                    ? "partially_connected"
                    : "awaiting_auth";

            // Resolve cache status
            var cacheService = _resolvedServices?.GetService<ITeamsCacheService>();
            var cacheStatus = cacheService?.GetStatus();

            var status = new SampleStatus(
                Status: "running",
                Message: "Teams Integration Sample is operational",
                TenantId: tenantId,
                Endpoint: endpoint,
                AuthMethod: authMethod,
                ConnectionState: connectionState,
                ConnectedBackends: connectedBackends,
                DeferredBackends: deferredBackends,
                CacheStats: cacheStatus is not null
                    ? $"{cacheStatus.PersonCount} people, {cacheStatus.ChatCount} chats, {cacheStatus.TeamCount} teams, {cacheStatus.ChannelCount} channels"
                    : "unavailable",
                Features:
                [
                    "Automatic caching",
                    "Cache short-circuiting",
                    "Credential scanning",
                    "Pagination enforcement",
                    includeVirtualTools ? "Virtual tools (exposed)" : "Virtual tools (hidden)"
                ]);

            return ValueTask.FromResult(new ModelContextProtocol.Protocol.CallToolResult
            {
                Content =
                [
                    new ModelContextProtocol.Protocol.TextContentBlock
                    {
                        Text = System.Text.Json.JsonSerializer.Serialize(status,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                    }
                ]
            });
        });
}

void PrintFeatures(string endpoint, AuthModeType mode)
{
    var output = mode != AuthModeType.ForwardAuth ? Console.Error : Console.Out;
    
    output.WriteLine("Teams integration features enabled:");
    output.WriteLine("  - Automatic caching of chats, teams, and people");
    output.WriteLine("  - Cache short-circuiting for ListChats, ListTeams, etc.");
    output.WriteLine("  - Credential scanning for outbound messages");
    output.WriteLine("  - Automatic pagination (top=20) for list operations");
    
    var authDescription = mode switch
    {
        AuthModeType.ProxyAuth => "Proxy-managed Azure AD authentication (client credentials)",
        AuthModeType.UserAuth => "User-delegated Azure AD authentication (interactive browser)",
        _ => "ForwardAuthorization (proxy is transparent, client manages auth)"
    };
    output.WriteLine($"  - {authDescription}");
    
    output.WriteLine();

    if (includeVirtualTools)
    {
        output.WriteLine("Virtual tools ENABLED (--include-virtual):");
        output.WriteLine("  - teams_resolve: Resolve names to Teams entities");
        output.WriteLine("  - teams_lookup_person: Look up person by ID/UPN");
        output.WriteLine("  - teams_lookup_chat: Look up chat by ID");
        output.WriteLine("  - teams_lookup_team: Look up team by ID");
        output.WriteLine("  - teams_lookup_channel: Look up channel by ID");
        output.WriteLine("  - teams_cache_status: Get cache statistics");
        output.WriteLine("  - teams_cache_refresh: Invalidate cache");
        output.WriteLine("  - teams_parse_url: Parse Teams URLs to extract IDs");
        output.WriteLine("  - teams_validate_message: Check message for credentials");
        output.WriteLine("  - teams_add_recent_contact: Add to recent contacts");
        output.WriteLine("  - teams_get_recent_contacts: Get recent contacts list");
    }
    else
    {
        output.WriteLine("Virtual tools hidden (pass --include-virtual to expose them).");
        output.WriteLine("The proxy handles caching, resolution, and validation transparently.");
    }

    output.WriteLine();
}

/// <summary>
/// Record for the sample status tool response.
/// </summary>
internal sealed record SampleStatus(
    string Status,
    string Message,
    string TenantId,
    string Endpoint,
    string AuthMethod,
    string ConnectionState,
    List<string> ConnectedBackends,
    List<string> DeferredBackends,
    string CacheStats,
    List<string> Features);

/// <summary>
/// Authentication mode for the Teams integration sample.
/// </summary>
internal enum AuthModeType
{
    /// <summary>HTTP transport, VS Code handles Azure AD authentication via OAuth.</summary>
    ForwardAuth,

    /// <summary>stdio transport, proxy authenticates with Azure AD using app credentials.</summary>
    ProxyAuth,

    /// <summary>stdio transport, proxy authenticates as the current user via interactive browser.</summary>
    UserAuth
}

// ═══════════════════════════════════════════════════════════════════════════
// Token Logger Middleware
// ═══════════════════════════════════════════════════════════════════════════
// Decodes the JWT from the first authenticated request and logs the claims
// needed for user-auth mode (client ID, audience, scopes, etc.).
// ═══════════════════════════════════════════════════════════════════════════

internal static class TokenLoggerExtensions
{
    /// <summary>
    /// Adds middleware that logs JWT claims from the first authenticated request.
    /// Use <c>--log-token</c> to enable. The claims are written to stderr as a
    /// one-shot operation; subsequent requests are passed through without logging.
    /// </summary>
    public static WebApplication UseTokenLogger(this WebApplication app)
    {
        var logged = 0; // 0 = not yet logged, 1 = already logged

        app.Use(async (context, next) =>
        {
            // Only log the first token we see
            if (Interlocked.CompareExchange(ref logged, 1, 0) == 0)
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

                if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader["Bearer ".Length..];
                    LogTokenClaims(token);
                }
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }

    /// <summary>
    /// Extracts the user principal name (or preferred_username/email) from a JWT access token.
    /// Returns null if the token cannot be decoded or the claim is not present.
    /// </summary>
    public static string? DecodeUpnFromToken(string jwt)
    {
        try
        {
            var claims = DecodeJwtPayload(jwt);
            if (claims is null)
            {
                return null;
            }

            return TryGetClaim(claims.Value, "upn")
                ?? TryGetClaim(claims.Value, "preferred_username")
                ?? TryGetClaim(claims.Value, "email");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Base64Url-decodes the JWT payload (middle segment) and returns it as a <see cref="JsonElement"/>.
    /// </summary>
    private static JsonElement? DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        // Base64Url -> Base64: replace URL-safe chars and pad
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Base64-decodes the JWT payload (middle segment) and logs the claims
    /// relevant to user-auth mode configuration.
    /// </summary>
    private static void LogTokenClaims(string jwt)
    {
        try
        {
            var claims = DecodeJwtPayload(jwt);
            if (claims is null)
            {
                Console.Error.WriteLine("[log-token] Invalid JWT format (expected 3 dot-separated segments).");
                return;
            }

            var appId = TryGetClaim(claims.Value, "appid") ?? TryGetClaim(claims.Value, "azp") ?? "(not found)";
            var audience = TryGetClaim(claims.Value, "aud") ?? "(not found)";
            var scopes = TryGetClaim(claims.Value, "scp") ?? "(not found)";
            var tenantId = TryGetClaim(claims.Value, "tid") ?? "(not found)";
            var upn = TryGetClaim(claims.Value, "upn")
                ?? TryGetClaim(claims.Value, "preferred_username")
                ?? TryGetClaim(claims.Value, "email")
                ?? "(not found)";
            var issuer = TryGetClaim(claims.Value, "iss") ?? "(not found)";

            Console.Error.WriteLine();
            Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine("TOKEN CLAIMS (from VS Code's first authenticated request)");
            Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine($"  appid (Client ID):   {appId}");
            Console.Error.WriteLine($"  aud   (Audience):    {audience}");
            Console.Error.WriteLine($"  scp   (Scopes):      {scopes}");
            Console.Error.WriteLine($"  tid   (Tenant ID):   {tenantId}");
            Console.Error.WriteLine($"  upn   (User):        {upn}");
            Console.Error.WriteLine($"  iss   (Issuer):      {issuer}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Use these values for user-auth mode:");
            Console.Error.WriteLine($"  --auth-mode=user-auth --vscode-client-id={appId}");

            // If scopes were found, suggest them too
            if (scopes != "(not found)")
            {
                // Azure AD scopes in JWT are space-separated; convert to audience-prefixed format
                // for use with MSAL token acquisition
                var scopeList = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (audience != "(not found)" && scopeList.Length > 0)
                {
                    var qualifiedScopes = string.Join(",",
                        scopeList.Select(s => s.Contains("://") ? s : $"{audience}/{s}"));
                    Console.Error.WriteLine($"  --scopes={qualifiedScopes}");
                }
            }

            Console.Error.WriteLine("═══════════════════════════════════════════════════════════════════════════");
            Console.Error.WriteLine();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[log-token] Failed to decode JWT: {ex.Message}");
        }
    }

    /// <summary>
    /// Tries to extract a string claim from the JWT payload.
    /// </summary>
    private static string? TryGetClaim(JsonElement claims, string name)
    {
        if (claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}
