using McpProxy.Core.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Create a host builder for the application
var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure MCP Proxy using the SDK fluent API
builder.Services.AddMcpProxy(proxy =>
{
    // Configure proxy server metadata
    proxy
        .WithServerInfo(
            name: "SDK Basic Example",
            version: "1.0.0",
            instructions: "This proxy is configured programmatically using the SDK")
        .WithToolCaching(enabled: true, ttlSeconds: 120);

    // Add a local STDIO server (filesystem access)
    // This demonstrates configuring a local MCP server process
    proxy.AddStdioServer(
            name: "filesystem",
            command: "npx",
            arguments: ["-y", "@anthropic/mcp-server-filesystem", "/workspace"])
        .WithTitle("Filesystem Server")
        .WithDescription("Provides secure file system access")
        .WithToolPrefix("fs")
        .DenyTools("delete_*", "remove_*", "unlink_*")  // Block dangerous operations
        .Build();

    // Add a remote SSE server
    // This demonstrates connecting to an external MCP server
    proxy.AddSseServer(
            name: "context7",
            url: "https://mcp.context7.com/sse")
        .WithTitle("Context7")
        .WithDescription("Documentation and code search")
        .Build();

    // Add a remote HTTP server with authentication
    // This demonstrates using environment variables for secrets
    var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrEmpty(githubToken))
    {
        proxy.AddHttpServer(
                name: "github",
                url: "https://api.github.com/mcp")
            .WithTitle("GitHub MCP")
            .WithDescription("GitHub API access")
            .WithHeaders(new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {githubToken}",
                ["User-Agent"] = "MCP-Proxy-SDK-Sample"
            })
            .WithToolPrefix("gh")
            .AllowTools("get_*", "list_*", "search_*")  // Only allow read operations
            .Build();
    }

    // You can also combine with a JSON configuration file
    // proxy.WithConfigurationFile("additional-servers.json");
});

// Build the host
var app = builder.Build();

// Initialize the proxy (connects to all configured backend servers)
Console.WriteLine("Initializing MCP Proxy...");
await app.Services.InitializeMcpProxyAsync();
Console.WriteLine("MCP Proxy initialized successfully!");

// Run the application
// In a real scenario, you would integrate with your MCP server transport
await app.RunAsync();
