using Azure.Identity;
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

        // Apply Teams integration hooks, interceptors, and virtual tools
        proxy.WithTeamsIntegration(configure: options =>
        {
            options.EnableAutoPagination = true;
            options.EnableCredentialScanning = true;
            options.EnableCachePopulation = true;
            options.EnableCacheShortCircuit = true;
            options.RegisterVirtualTools = true;
        });
    });

    // Configure MCP Server with stdio transport (no auth required from clients)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithSdkProxyHandlers();

    var app = builder.Build();

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
// FORWARD-AUTH MODE: HTTP transport with VS Code-managed authentication
// ═══════════════════════════════════════════════════════════════════════════
async Task RunForwardAuthModeAsync(string tenantId, int port, string teamsServerUrl)
{
    Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.WriteLine("Teams Integration Sample - INTERACTIVE BROWSER AUTH MODE (HTTP)");
    Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    Console.WriteLine();

    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls($"http://localhost:{port}");

    // Configure logging
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Add Teams integration services
    builder.Services.AddTeamsIntegration(ConfigureTeamsIntegration);

    // Discover the correct scope and authorization server for the Teams MCP Server.
    //
    // When VS Code connects directly, it uses RFC 9728 OAuth Protected Resource Metadata
    // to discover the authorization server and scopes. We do the same probe here.
    //
    // The metadata endpoint returns:
    //   - authorization_servers: ["https://login.microsoftonline.com/organizations/v2.0"]
    //   - scopes_supported: ["{resource}/.default", "openid", "profile", "offline_access"]
    //
    // We then create an InteractiveBrowserCredential that opens the user's browser for
    // sign-in and consent — this is essential because the Teams backend requires the
    // 'McpServers.Teams.All' delegated permission, which must be consented to interactively.
    //
    // The user can override the scope via --scope or AZURE_SCOPE if the probe fails.
    var scopeOverride = Environment.GetEnvironmentVariable("AZURE_SCOPE")
        ?? GetArg(args, "--scope");

    string teamsScope;
    string? authorizationServer;

    if (scopeOverride is not null)
    {
        teamsScope = scopeOverride;
        authorizationServer = null;
        Console.WriteLine($"Using override scope: {teamsScope}");
    }
    else
    {
        var discovery = await DiscoverTeamsOAuthMetadataAsync(teamsServerUrl).ConfigureAwait(false);
        teamsScope = discovery.Scope;
        authorizationServer = discovery.AuthorizationServer;
    }

    Console.WriteLine($"Token scope: {teamsScope}");

    // Create an InteractiveBrowserCredential that opens the user's browser.
    // This triggers the Azure AD consent flow so the user can grant 'McpServers.Teams.All'.
    // We use the Azure CLI's well-known public client ID since it is a first-party Microsoft
    // app pre-authorized for delegated flows and does not require app registration.
    var credentialOptions = new InteractiveBrowserCredentialOptions
    {
        TenantId = tenantId,
        ClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46", // Azure CLI public client
    };

    // If we discovered an authorization server, extract the authority host from it.
    // The RFC 9728 metadata returns e.g. "https://login.microsoftonline.com/organizations/v2.0"
    // but Azure.Identity needs just the host: "https://login.microsoftonline.com".
    if (authorizationServer is not null)
    {
        var authUri = new Uri(authorizationServer);
        credentialOptions.AuthorityHost = new Uri($"{authUri.Scheme}://{authUri.Host}");
        Console.WriteLine($"Authorization server: {authorizationServer}");
        Console.WriteLine($"Authority host: {credentialOptions.AuthorityHost}");
    }

    var credential = new InteractiveBrowserCredential(credentialOptions);

    Console.WriteLine();
    Console.WriteLine("A browser window will open for sign-in when the first token is requested.");
    Console.WriteLine("Please grant consent for the Teams MCP Server permissions when prompted.");
    Console.WriteLine();

    // Configure MCP Proxy with InteractiveBrowserCredential authentication
    builder.Services.AddMcpProxy(proxy =>
    {
        proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
            "MCP Proxy with Teams caching, credential scanning, and virtual tools. " +
            "Running with InteractiveBrowserCredential - proxy authenticates via browser sign-in.");

        // Connect to Teams MCP Server using InteractiveBrowserCredential
        proxy.AddSseServer("teams", teamsServerUrl)
            .WithTitle("Microsoft Teams")
            .WithDescription("Microsoft Teams MCP Server for chat, messaging, and collaboration")
            .WithToolPrefix("teams")
            .WithBackendAuth(BackendAuthType.AzureDefaultCredential, azureAd =>
            {
                azureAd.Scopes = [teamsScope];
                azureAd.TokenCredential = credential;
            })
            .Build();

        AddStatusTool(proxy, tenantId, $"http://localhost:{port}/mcp", "InteractiveBrowserCredential");

        // Apply Teams integration hooks, interceptors, and virtual tools
        proxy.WithTeamsIntegration(configure: options =>
        {
            options.EnableAutoPagination = true;
            options.EnableCredentialScanning = true;
            options.EnableCachePopulation = true;
            options.EnableCacheShortCircuit = true;
            options.RegisterVirtualTools = true;
        });
    });

    // Configure MCP Server with HTTP transport and proxy handlers
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithSdkProxyHandlers();

    var app = builder.Build();

    // Initialize cache
    await InitializeCacheAsync(app.Services).ConfigureAwait(false);

    // Initialize MCP Proxy
    Console.WriteLine();
    Console.WriteLine("Initializing MCP Proxy with Teams integration...");
    await app.InitializeMcpProxyAsync().ConfigureAwait(false);
    Console.WriteLine("MCP Proxy initialized!");
    Console.WriteLine();
    Console.WriteLine($"Tenant ID: {tenantId}");
    Console.WriteLine($"Token scope: {teamsScope}");
    Console.WriteLine($"Auth method: InteractiveBrowserCredential (browser sign-in)");
    Console.WriteLine($"Proxy URL: http://localhost:{port}/mcp");
    Console.WriteLine();

    PrintFeatures($"http://localhost:{port}/mcp", useProxyAuth: false);

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
        output.WriteLine("  - Authorization via InteractiveBrowserCredential (browser sign-in)");
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

// ═══════════════════════════════════════════════════════════════════════════
// Scope Discovery via RFC 9728 OAuth Protected Resource Metadata
// ═══════════════════════════════════════════════════════════════════════════
//
// The metadata endpoint is constructed per RFC 9728 Section 3:
// /.well-known/oauth-protected-resource is inserted between the host and path.
// For example:
//   https://agent365.svc.cloud.microsoft/agents/tenants/{tid}/servers/mcp_TeamsServer
// becomes:
//   https://agent365.svc.cloud.microsoft/.well-known/oauth-protected-resource/agents/tenants/{tid}/servers/mcp_TeamsServer
//
// The response typically contains:
//   {
//     "resource": "https://agent365.svc.cloud.microsoft/agents/tenants/{tid}/servers/mcp_TeamsServer",
//     "authorization_servers": ["https://login.microsoftonline.com/organizations/v2.0"],
//     "scopes_supported": ["{appIdUri}/.default", "openid", "profile", "offline_access"]
//   }
//
// We extract:
//   1. The authorization server URL (used to construct the InteractiveBrowserCredential)
//   2. The scopes to request (the first /.default scope for token audience)
// ═══════════════════════════════════════════════════════════════════════════
static async Task<TeamsOAuthDiscoveryResult> DiscoverTeamsOAuthMetadataAsync(string teamsServerUrl)
{
    var uri = new Uri(teamsServerUrl);
    var metadataUrl = $"{uri.Scheme}://{uri.Authority}/.well-known/oauth-protected-resource{uri.AbsolutePath}";
    var fallbackScope = $"{uri.Scheme}://{uri.Host}/.default";

    Console.WriteLine($"Probing RFC 9728 metadata: {metadataUrl}");

    try
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"RFC 9728 probe returned {(int)response.StatusCode}, using fallback scope.");
            return new TeamsOAuthDiscoveryResult(fallbackScope, null);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.WriteLine($"RFC 9728 metadata: {json}");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Extract authorization server
        string? authorizationServer = null;
        if (root.TryGetProperty("authorization_servers", out var authServersElement)
            && authServersElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var server in authServersElement.EnumerateArray())
            {
                authorizationServer = server.GetString();
                if (authorizationServer is not null)
                {
                    Console.WriteLine($"Discovered authorization server: {authorizationServer}");
                    break;
                }
            }
        }

        // Extract scope — prefer /.default, then first non-OIDC scope, then resource + /.default
        string? scope = null;

        if (root.TryGetProperty("scopes_supported", out var scopesElement)
            && scopesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var s in scopesElement.EnumerateArray())
            {
                var scopeStr = s.GetString();
                if (scopeStr is not null && scopeStr.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
                {
                    scope = scopeStr;
                    Console.WriteLine($"Discovered scope: {scope}");
                    break;
                }
            }

            if (scope is null)
            {
                foreach (var s in scopesElement.EnumerateArray())
                {
                    var scopeStr = s.GetString();
                    if (scopeStr is not null
                        && scopeStr != "openid"
                        && scopeStr != "profile"
                        && scopeStr != "offline_access"
                        && scopeStr != "email")
                    {
                        scope = scopeStr;
                        Console.WriteLine($"No /.default scope found. Using: {scope}");
                        break;
                    }
                }
            }
        }

        if (scope is null && root.TryGetProperty("resource", out var resourceElement))
        {
            var resource = resourceElement.GetString();
            if (resource is not null)
            {
                scope = $"{resource.TrimEnd('/')}/.default";
                Console.WriteLine($"Using resource field: {scope}");
            }
        }

        return new TeamsOAuthDiscoveryResult(scope ?? fallbackScope, authorizationServer);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"RFC 9728 probe failed: {ex.Message}. Using fallback scope.");
        return new TeamsOAuthDiscoveryResult(fallbackScope, null);
    }
}

// Result of RFC 9728 OAuth metadata discovery.
record TeamsOAuthDiscoveryResult(string Scope, string? AuthorizationServer);

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
