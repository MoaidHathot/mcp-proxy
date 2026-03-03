# SDK Basic Usage

This example demonstrates how to use MCP Proxy as an SDK in your .NET applications, providing programmatic configuration instead of JSON files.

## Features Demonstrated

- **Fluent Builder API**: Configure the proxy using code
- **Multiple Transport Types**: STDIO, HTTP, and SSE backends
- **Server Configuration**: Environment variables, headers, prefixes
- **Tool Filtering**: Allowlist and denylist via code
- **Dependency Injection**: Integration with ASP.NET Core

## When to Use SDK vs JSON Configuration

| Use Case | Recommended Approach |
|----------|---------------------|
| Static configuration | JSON file |
| Dynamic server discovery | SDK |
| Custom authentication logic | SDK |
| Runtime tool modification | SDK |
| Integration with existing apps | SDK |
| CI/CD deployments | JSON file |

## Project Structure

```
11-sdk-basic/
├── Program.cs           # Main entry point with SDK configuration
├── SdkBasic.csproj     # Project file
└── README.md           # This file
```

## Code Example

```csharp
using McpProxy.Core.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Configure MCP Proxy using the SDK
builder.Services.AddMcpProxy(proxy =>
{
    // Configure proxy server info
    proxy
        .WithServerInfo("My MCP Proxy", "1.0.0", "A custom MCP proxy server")
        .WithToolCaching(enabled: true, ttlSeconds: 120);

    // Add a local STDIO server
    proxy.AddStdioServer("filesystem", "npx", "-y", "@anthropic/mcp-server-filesystem", "/workspace")
        .WithTitle("Filesystem Server")
        .WithDescription("Provides file system access")
        .WithToolPrefix("fs")
        .DenyTools("delete_*", "remove_*")  // Block dangerous operations
        .Build();

    // Add a remote SSE server
    proxy.AddSseServer("context7", "https://mcp.context7.com/sse")
        .WithTitle("Context7")
        .WithDescription("Documentation search")
        .Build();

    // Add a remote HTTP server with authentication
    proxy.AddHttpServer("github", "https://api.github.com/mcp")
        .WithTitle("GitHub MCP")
        .WithHeaders(new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {Environment.GetEnvironmentVariable("GITHUB_TOKEN")}"
        })
        .WithToolPrefix("gh")
        .AllowTools("get_*", "list_*", "search_*")  // Only read operations
        .Build();
});

var app = builder.Build();

// Initialize the proxy (connects to all backend servers)
await app.Services.InitializeMcpProxyAsync();

await app.RunAsync();
```

## Key SDK Methods

### IMcpProxyBuilder

| Method | Description |
|--------|-------------|
| `WithServerInfo(name, version, instructions)` | Set proxy server metadata |
| `WithToolCaching(enabled, ttlSeconds)` | Configure tool list caching |
| `AddStdioServer(name, command, args...)` | Add a local STDIO backend |
| `AddHttpServer(name, url)` | Add a remote HTTP backend |
| `AddSseServer(name, url)` | Add a remote SSE backend |
| `WithConfigurationFile(path)` | Merge with JSON configuration |

### IServerBuilder

| Method | Description |
|--------|-------------|
| `WithTitle(title)` | Set display name |
| `WithDescription(description)` | Set description |
| `WithEnvironment(dict)` | Set environment variables (STDIO) |
| `WithHeaders(dict)` | Set HTTP headers (HTTP/SSE) |
| `WithToolPrefix(prefix, separator)` | Add prefix to tool names |
| `AllowTools(patterns...)` | Only include matching tools |
| `DenyTools(patterns...)` | Exclude matching tools |
| `Enabled(bool)` | Enable/disable the server |
| `Build()` | Return to proxy builder |

## Running the Example

```bash
# Build the project
dotnet build

# Run with required environment variables
GITHUB_TOKEN=your_token dotnet run
```

## Combining SDK with JSON Configuration

You can combine SDK configuration with a JSON file. SDK settings take precedence:

```csharp
proxy
    .WithConfigurationFile("mcp-proxy.json")  // Load base config
    .AddStdioServer("custom", "my-server")    // Add SDK-only server
    .Build();
```

## ASP.NET Core Integration

For HTTP/SSE transport, integrate with ASP.NET Core:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add MCP Proxy services
builder.Services.AddMcpProxy(proxy =>
{
    proxy.AddStdioServer("backend", "node", "server.js").Build();
});

var app = builder.Build();

// Initialize proxy
await app.Services.InitializeMcpProxyAsync();

// Map MCP endpoints
app.MapMcp("/mcp").WithSdkProxyHandlers();

await app.RunAsync();
```

## Next Steps

- See [12-sdk-hooks-interceptors](../12-sdk-hooks-interceptors) for custom hooks and interceptors
- See [13-sdk-virtual-tools](../13-sdk-virtual-tools) for virtual tools and tool modification
