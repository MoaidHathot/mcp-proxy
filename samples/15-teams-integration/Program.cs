using McpProxy.Core.Configuration;
using McpProxy.Core.Sdk;
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
// 1. FORWARD-AUTH (default): HTTP/SSE mode where VS Code authenticates with
//    Azure AD and the proxy forwards the Authorization header to Teams.
//    Best for: Interactive use where users authenticate themselves.
//
// 2. PROXY-AUTH: stdio mode where the proxy authenticates with Azure AD
//    using app credentials. Clients connect without authentication.
//    Best for: Automated agents, scripts, or simplified client setup.
//
// ═══════════════════════════════════════════════════════════════════════════

// Parse command-line arguments and environment variables
var tenantId = Environment.GetEnvironmentVariable("TENANT_ID")
    ?? args.FirstOrDefault(a => a.StartsWith("--tenant-id="))?.Split('=')[1]
    ?? throw new InvalidOperationException(
        "Tenant ID is required. Set TENANT_ID environment variable or pass --tenant-id=<your-tenant-id>");

var authMode = Environment.GetEnvironmentVariable("AUTH_MODE")?.ToLowerInvariant()
    ?? args.FirstOrDefault(a => a.StartsWith("--auth-mode="))?.Split('=')[1]?.ToLowerInvariant()
    ?? "forward-auth";

var port = int.TryParse(
    Environment.GetEnvironmentVariable("PORT") ?? args.FirstOrDefault(a => a.StartsWith("--port="))?.Split('=')[1],
    out var p) ? p : 5100;

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
        ?? args.FirstOrDefault(a => a.StartsWith("--client-id="))?.Split('=')[1]
        ?? throw new InvalidOperationException(
            "AZURE_CLIENT_ID is required for proxy-auth mode. " +
            "Set the environment variable or pass --client-id=<your-app-client-id>");

    // Client secret can use env: prefix for environment variable lookup
    clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET")
        ?? args.FirstOrDefault(a => a.StartsWith("--client-secret="))?.Split('=')[1]
        ?? "env:AZURE_CLIENT_SECRET"; // Default to env lookup

    // Scopes for Teams MCP Server (may need adjustment based on actual API requirements)
    var scopeValue = Environment.GetEnvironmentVariable("AZURE_SCOPES")
        ?? args.FirstOrDefault(a => a.StartsWith("--scopes="))?.Split('=')[1];
    
    scopes = scopeValue?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? ["https://graph.microsoft.com/.default"];
}

var teamsServerUrl = $"https://agent365.svc.cloud.microsoft/agents/tenants/{tenantId}/servers/mcp_TeamsServer";

// ═══════════════════════════════════════════════════════════════════════════
// Application Configuration
// ═══════════════════════════════════════════════════════════════════════════

if (useProxyAuth)
{
    // PROXY-AUTH MODE: stdio transport, proxy handles Azure AD authentication
    await RunProxyAuthModeAsync(tenantId, clientId!, clientSecret!, scopes!, teamsServerUrl);
}
else
{
    // FORWARD-AUTH MODE: HTTP transport, VS Code handles Azure AD authentication
    await RunForwardAuthModeAsync(tenantId, port, teamsServerUrl);
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

    // Add Teams integration services
    builder.Services.AddTeamsIntegration(ConfigureTeamsIntegration);

    // Configure MCP Proxy with Azure AD Client Credentials auth
    builder.Services.AddMcpProxy(proxy =>
    {
        proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
            "MCP Proxy with Teams caching, credential scanning, and virtual tools. " +
            "Running in proxy-auth mode - no client authentication required.");

        // Connect to Teams MCP Server with proxy-managed Azure AD authentication
        proxy.AddSseServer("teams", teamsServerUrl)
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
    });

    // Configure MCP Server with stdio transport (no auth required from clients)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithSdkProxyHandlers();

    var app = builder.Build();

    // Initialize cache
    await InitializeCacheAsync(app.Services);

    // Apply Teams integration
    ApplyTeamsIntegration(app.Services);

    // Initialize MCP Proxy
    Console.Error.WriteLine("Initializing MCP Proxy with Teams integration...");
    await app.InitializeMcpProxyAsync().ConfigureAwait(false);
    Console.Error.WriteLine("MCP Proxy initialized!");
    Console.Error.WriteLine();

    PrintFeatures("stdio (proxy-auth)", useProxyAuth: true);

    await app.RunAsync().ConfigureAwait(false);
}

// ═══════════════════════════════════════════════════════════════════════════
// FORWARD-AUTH MODE: HTTP transport with VS Code-managed authentication
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

    // Add HTTP context accessor (required for ForwardAuthorization)
    builder.Services.AddHttpContextAccessor();

    // Add Teams integration services
    builder.Services.AddTeamsIntegration(ConfigureTeamsIntegration);

    // Configure MCP Proxy with Authorization header forwarding
    builder.Services.AddMcpProxy(proxy =>
    {
        proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
            "MCP Proxy with Teams caching, credential scanning, and virtual tools. " +
            "Running in forward-auth mode - authenticate via VS Code.");

        // Connect to Teams MCP Server with Authorization header forwarding
        proxy.AddSseServer("teams", teamsServerUrl)
            .WithTitle("Microsoft Teams")
            .WithDescription("Microsoft Teams MCP Server for chat, messaging, and collaboration")
            .WithToolPrefix("teams")
            .WithBackendAuth(BackendAuthType.ForwardAuthorization)
            .Build();

        AddStatusTool(proxy, tenantId, $"http://localhost:{port}/mcp", "Authorization header forwarding");
    });

    // Configure MCP Server with HTTP transport
    builder.Services
        .AddMcpServer()
        .WithHttpTransport();

    var app = builder.Build();

    // Initialize cache
    await InitializeCacheAsync(app.Services);

    // Apply Teams integration
    ApplyTeamsIntegration(app.Services);

    // Initialize MCP Proxy
    Console.WriteLine();
    Console.WriteLine("Initializing MCP Proxy with Teams integration...");
    await app.InitializeMcpProxyAsync().ConfigureAwait(false);
    Console.WriteLine("MCP Proxy initialized!");
    Console.WriteLine();
    Console.WriteLine($"Tenant ID: {tenantId}");
    Console.WriteLine($"Proxy URL: http://localhost:{port}/mcp");
    Console.WriteLine();

    PrintFeatures($"http://localhost:{port}/mcp", useProxyAuth: false);

    // Enable OAuth metadata proxy middleware (auto-detects from backend)
    app.UseOAuthMetadataProxy(cacheDuration: TimeSpan.FromMinutes(15));

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

void ApplyTeamsIntegration(IServiceProvider services)
{
    var proxyBuilder = services.GetRequiredService<McpProxy.Abstractions.IMcpProxyBuilder>();
    proxyBuilder.WithTeamsIntegration(services, options =>
    {
        options.EnableAutoPagination = true;
        options.EnableCredentialScanning = true;
        options.EnableCachePopulation = true;
        options.EnableCacheShortCircuit = true;
        options.RegisterVirtualTools = true;
    });
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
        description: "Get the status of the Teams integration sample",
        handler: (request, ct) =>
        {
            var status = new SampleStatus(
                Status: "running",
                Message: "Teams Integration Sample is operational",
                TenantId: tenantId,
                Endpoint: endpoint,
                AuthMethod: authMethod,
                Features:
                [
                    "Automatic caching",
                    "Cache short-circuiting",
                    "Credential scanning",
                    "Pagination enforcement",
                    "Virtual tools"
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
        output.WriteLine("  - Proxy-managed Azure AD authentication");
    }
    else
    {
        output.WriteLine("  - Authorization header forwarding to Teams MCP Server");
    }
    
    output.WriteLine();
    output.WriteLine("Virtual tools available:");
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
    List<string> Features);
