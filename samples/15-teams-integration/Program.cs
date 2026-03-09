using McpProxy.Core.Sdk;
using McpProxy.Samples.TeamsIntegration;
using McpProxy.Samples.TeamsIntegration.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
// ═══════════════════════════════════════════════════════════════════════════

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add Teams integration services (for DI access to cache service)
builder.Services.AddTeamsIntegration(options =>
{
    // Configure cache settings
    options.CacheTtl = TimeSpan.FromHours(4);
    options.MaxRecentContacts = 50;

    // Configure hooks
    options.EnableAutoPagination = true;
    options.DefaultPaginationLimit = 20;

    options.EnableCredentialScanning = true;
    options.BlockCredentials = true;

    options.EnableMessagePrefix = false; // Set to true to prefix with "[AI]"
    options.MessagePrefix = "[AI]";

    // Configure caching behavior
    options.EnableCachePopulation = true;
    options.EnableCacheShortCircuit = true;
    options.LoadCacheOnStartup = true;
    options.AutoSaveCache = false; // Manual save for performance
});

// Configure MCP Proxy with Teams integration
builder.Services.AddMcpProxy(proxy =>
{
    proxy.WithServerInfo("Teams Integration Sample", "1.0.0",
        "MCP Proxy with Teams caching, credential scanning, and virtual tools.");

    // ═══════════════════════════════════════════════════════════════════════
    // Teams Integration Configuration
    // ═══════════════════════════════════════════════════════════════════════
    // Note: In a real application, you would call WithTeamsIntegration here
    // after building the service provider. For this sample, we show the
    // manual setup approach.

    // Manual setup example (alternative to extension method):
    // This shows what WithTeamsIntegration does internally

    // Add a placeholder Teams MCP server (replace with actual Teams MCP server)
    // proxy.AddStdioServer("teams", "npx", "-y", "@example/mcp-server-msgraph")
    //     .WithTitle("Microsoft Teams")
    //     .WithToolPrefix("teams")
    //     .Build();

    // For demo purposes, add a simple status tool
    proxy.AddTool(
        name: "sample_status",
        description: "Get the status of the Teams integration sample",
        handler: (request, ct) =>
        {
            var status = new SampleStatus(
                Status: "running",
                Message: "Teams Integration Sample is operational",
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
});

// Build the application
var app = builder.Build();

// Initialize Teams cache (load from disk)
var cacheService = app.Services.GetService<ITeamsCacheService>();
if (cacheService is not null)
{
    Console.WriteLine("Loading Teams cache from disk...");
    await cacheService.LoadAsync().ConfigureAwait(false);
    var status = cacheService.GetStatus();
    Console.WriteLine($"Cache loaded: {status.PersonCount} people, {status.ChatCount} chats, " +
                     $"{status.TeamCount} teams, {status.ChannelCount} channels");
}

// Apply Teams integration to the proxy
// This must happen after building the host so we have access to services
var serviceProvider = app.Services;
var proxyBuilder = serviceProvider.GetRequiredService<McpProxy.Abstractions.IMcpProxyBuilder>();
proxyBuilder.WithTeamsIntegration(serviceProvider, options =>
{
    options.EnableAutoPagination = true;
    options.EnableCredentialScanning = true;
    options.EnableCachePopulation = true;
    options.EnableCacheShortCircuit = true;
    options.RegisterVirtualTools = true;
});

// Initialize MCP Proxy
Console.WriteLine();
Console.WriteLine("Initializing MCP Proxy with Teams integration...");
await app.InitializeMcpProxyAsync().ConfigureAwait(false);
Console.WriteLine("MCP Proxy initialized!");
Console.WriteLine();
Console.WriteLine("Teams integration features enabled:");
Console.WriteLine("  - Automatic caching of chats, teams, and people");
Console.WriteLine("  - Cache short-circuiting for ListChats, ListTeams, etc.");
Console.WriteLine("  - Credential scanning for outbound messages");
Console.WriteLine("  - Automatic pagination (top=20) for list operations");
Console.WriteLine();
Console.WriteLine("Virtual tools available:");
Console.WriteLine("  - teams_resolve: Resolve names to Teams entities");
Console.WriteLine("  - teams_lookup_person: Look up person by ID/UPN");
Console.WriteLine("  - teams_lookup_chat: Look up chat by ID");
Console.WriteLine("  - teams_lookup_team: Look up team by ID");
Console.WriteLine("  - teams_lookup_channel: Look up channel by ID");
Console.WriteLine("  - teams_cache_status: Get cache statistics");
Console.WriteLine("  - teams_cache_refresh: Invalidate cache");
Console.WriteLine("  - teams_parse_url: Parse Teams URLs to extract IDs");
Console.WriteLine("  - teams_validate_message: Check message for credentials");
Console.WriteLine("  - teams_add_recent_contact: Add to recent contacts");
Console.WriteLine("  - teams_get_recent_contacts: Get recent contacts list");
Console.WriteLine();

// Handle shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    if (cacheService is not null)
    {
        Console.WriteLine("Saving Teams cache to disk...");
        cacheService.SaveAsync().GetAwaiter().GetResult();
        Console.WriteLine("Cache saved.");
    }
});

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Record for the sample status tool response.
/// </summary>
internal sealed record SampleStatus(string Status, string Message, List<string> Features);
