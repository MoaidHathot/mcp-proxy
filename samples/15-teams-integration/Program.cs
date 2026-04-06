using McpProxy.SDK.Configuration;
using McpProxy.SDK.Proxy;
using McpProxy.SDK.Sdk;
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

// Validate auth mode
var useProxyAuth = authMode switch
{
    "proxy-auth" => true,
    "forward-auth" => false,
    _ => throw new InvalidOperationException(
        $"Invalid AUTH_MODE: '{authMode}'. Valid values: 'forward-auth' (default), 'proxy-auth'")
};

// For proxy-auth mode, we need Azure AD app credentials
string? clientId = null;
string? clientSecret = null;
string[]? scopes = null;

if (useProxyAuth)
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

var teamsServerUrl = $"https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/mcp_TeamsServer";

// Lazy reference to the service provider, set after Build() completes.
// This allows virtual tool handlers (which run at request time) to resolve DI services.
IServiceProvider? _resolvedServices = null;

// ═══════════════════════════════════════════════════════════════════════════
// Application Configuration
// ═══════════════════════════════════════════════════════════════════════════

if (useProxyAuth)
{
    // PROXY-AUTH MODE: stdio transport, proxy handles Azure AD authentication
    await RunProxyAuthModeAsync(tenantId, clientId!, clientSecret!, scopes!, teamsServerUrl).ConfigureAwait(false);
}
else
{
    // FORWARD-AUTH MODE: HTTP transport, VS Code handles Azure AD authentication
    await RunForwardAuthModeAsync(tenantId, port, teamsServerUrl).ConfigureAwait(false);
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

    PrintFeatures("stdio (proxy-auth)", useProxyAuth: true);

    await app.RunAsync().ConfigureAwait(false);
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
    Console.WriteLine();

    PrintFeatures($"http://localhost:{port}/mcp", useProxyAuth: false);

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

void PrintFeatures(string endpoint, bool useProxyAuth)
{
    var output = useProxyAuth ? Console.Error : Console.Out;
    
    output.WriteLine("Teams integration features enabled:");
    output.WriteLine("  - Automatic caching of chats, teams, and people");
    output.WriteLine("  - Cache short-circuiting for ListChats, ListTeams, etc.");
    output.WriteLine("  - Credential scanning for outbound messages");
    output.WriteLine("  - Automatic pagination (top=20) for list operations");
    
    if (useProxyAuth)
    {
        output.WriteLine("  - Proxy-managed Azure AD authentication (client credentials)");
    }
    else
    {
        output.WriteLine("  - ForwardAuthorization (proxy is transparent, client manages auth)");
    }
    
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
