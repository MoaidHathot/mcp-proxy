---
layout: default
title: API Reference
description: Programmatic usage of MCP Proxy
---

## API Reference

MCP Proxy can be used as a library in your .NET applications for programmatic control over MCP server aggregation.

<div class="toc">
<h4>On this page</h4>
<ul>
<li><a href="#installation">Installation</a></li>
<li><a href="#basic-usage">Basic Usage</a></li>
<li><a href="#configuration">Configuration</a></li>
<li><a href="#client-management">Client Management</a></li>
<li><a href="#proxy-server">Proxy Server</a></li>
<li><a href="#filtering">Filtering</a></li>
<li><a href="#hooks">Hooks</a></li>
<li><a href="#extension-points">Extension Points</a></li>
</ul>
</div>

## Installation

Add the McpProxy.Core package to your project:

```bash
dotnet add package McpProxy.Core
```

Or add to your `.csproj`:

```xml
<PackageReference Include="McpProxy.Core" Version="1.0.0" />
```

## Basic Usage

```csharp
using McpProxy.Core.Configuration;
using McpProxy.Core.Proxy;
using Microsoft.Extensions.Logging;

// Create logger factory
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Load configuration
var config = await ConfigurationLoader.LoadAsync("mcp-proxy.json");

// Create client manager
var clientManager = new McpClientManager(
    loggerFactory.CreateLogger<McpClientManager>(),
    proxyClientHandlers,
    notificationForwarder);

// Initialize connections to backend servers
await clientManager.InitializeAsync(config, cancellationToken);

// Create proxy server
var proxyServer = new McpProxyServer(
    loggerFactory.CreateLogger<McpProxyServer>(),
    clientManager,
    config
);

// List all tools from all backends
var tools = await proxyServer.ListToolsAsync(requestContext, cancellationToken);

foreach (var tool in tools)
{
    Console.WriteLine($"Tool: {tool.Name} - {tool.Description}");
}

// Cleanup
await clientManager.DisposeAsync();
```

## Configuration

### Loading Configuration

```csharp
// From file (async)
var config = await ConfigurationLoader.LoadAsync("mcp-proxy.json", cancellationToken);

// From string
var json = File.ReadAllText("mcp-proxy.json");
var config = ConfigurationLoader.LoadFromString(json);

// Programmatic configuration
var config = new ProxyConfiguration
{
    Proxy = new ProxySettings
    {
        ServerInfo = new ServerInfo
        {
            Name = "My Proxy",
            Version = "1.0.0"
        }
    },
    Mcp = new Dictionary<string, ServerConfiguration>
    {
        ["my-server"] = new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "node",
            Arguments = ["server.js"]
        }
    }
};
```

### Server Configuration

```csharp
var serverConfig = new ServerConfiguration
{
    Type = ServerTransportType.Stdio,
    Title = "My Server",
    Description = "A local MCP server",
    Command = "my-server",
    Arguments = ["--port", "3000"],
    Environment = new Dictionary<string, string>
    {
        ["API_KEY"] = Environment.GetEnvironmentVariable("API_KEY")
    },
    Enabled = true,
    Tools = new ToolsConfiguration
    {
        Prefix = "myserver",
        PrefixSeparator = "_",
        Filter = new FilterConfiguration
        {
            Mode = FilterMode.AllowList,
            Patterns = ["read_*", "write_*"]
        }
    }
};
```

## Client Management

### McpClientManager

Manages connections to backend MCP servers.

```csharp
var clientManager = new McpClientManager(
    loggerFactory.CreateLogger<McpClientManager>(),
    proxyClientHandlers,     // optional
    notificationForwarder    // optional
);

// Initialize from configuration
await clientManager.InitializeAsync(config, cancellationToken);

// Get all connected clients
var clients = clientManager.Clients;

// Get a specific client
var client = clientManager.GetClient("server-name");

// Dispose (closes all connections)
await clientManager.DisposeAsync();
```

### ProxyClientHandlers

Forward sampling, elicitation, and roots requests to connected clients.

```csharp
var handlers = new ProxyClientHandlers(mcpServer, logger);

// Set up handlers when creating backend connections
var clientOptions = new McpClientOptions
{
    Handlers = new McpClientHandlers
    {
        SamplingHandler = handlers.HandleSamplingAsync,
        ElicitationHandler = handlers.HandleElicitationAsync,
        RootsHandler = handlers.HandleRootsAsync
    }
};
```

## Proxy Server

### McpProxyServer

The main proxy class that aggregates multiple backends.

```csharp
var proxyServer = new McpProxyServer(
    loggerFactory.CreateLogger<McpProxyServer>(),
    clientManager,
    config
);

// List operations (require RequestContext from MCP server)
var tools = await proxyServer.ListToolsAsync(requestContext, cancellationToken);
var resources = await proxyServer.ListResourcesAsync(requestContext, cancellationToken);
var prompts = await proxyServer.ListPromptsAsync(requestContext, cancellationToken);

// Tool operations
var result = await proxyServer.CallToolAsync(requestContext, cancellationToken);

// Resource operations
var content = await proxyServer.ReadResourceAsync(requestContext, cancellationToken);

// Prompt operations
var prompt = await proxyServer.GetPromptAsync(requestContext, cancellationToken);

// Add hook pipeline for a server
proxyServer.AddHookPipeline("server-name", hookPipeline);
```

### SingleServerProxy

Proxy for a single backend server (used for per-server routing).

```csharp
var singleProxy = new SingleServerProxy(
    loggerFactory.CreateLogger<SingleServerProxy>(),
    clientManager,
    serverName,
    serverConfiguration
);

// Set hook pipeline (optional)
singleProxy.SetHookPipeline(hookPipeline);

// Operations for a single server
var tools = await singleProxy.ListToolsAsync(cancellationToken);
var result = await singleProxy.CallToolAsync(callToolParams, cancellationToken);
var resources = await singleProxy.ListResourcesAsync(cancellationToken);
var prompts = await singleProxy.ListPromptsAsync(cancellationToken);
```

## Filtering

### Built-in Filters

```csharp
// No filtering
IToolFilter noFilter = new NoFilter();

// AllowList
IToolFilter allowList = new AllowListFilter(
    patterns: ["read_*", "write_*"],
    caseInsensitive: true
);

// DenyList
IToolFilter denyList = new DenyListFilter(
    patterns: ["delete_*", "drop_*"],
    caseInsensitive: true
);

// Regex
IToolFilter regex = new RegexFilter(
    includePattern: "^(read|write)_.*$",
    excludePattern: ".*_internal$"
);

// Using the factory
var filter = FilterFactory.Create(new FilterConfiguration
{
    Mode = FilterMode.AllowList,
    Patterns = ["read_*"]
});
```

### Custom Filters

```csharp
public class CustomFilter : IToolFilter
{
    public bool ShouldInclude(Tool tool)
    {
        // Custom logic
        return tool.Name.StartsWith("allowed_");
    }
}

// Use custom filter directly
IToolFilter filter = new CustomFilter();
```

> **Note**: The `FilterFactory` is a static class that creates built-in filters from configuration. For custom filters, implement `IToolFilter` directly and use it in your code.

### Resource and Prompt Filters

```csharp
// Resource filtering
IResourceFilter resourceFilter = new ResourceAllowListFilter(
    patterns: ["file://*"],
    caseInsensitive: true
);

// Prompt filtering
IPromptFilter promptFilter = new PromptDenyListFilter(
    patterns: ["internal_*"],
    caseInsensitive: true
);
```

## Hooks

### Using the Hook Pipeline

```csharp
var pipeline = new HookPipeline(loggerFactory.CreateLogger<HookPipeline>());

// Add built-in hooks
pipeline.AddPreInvokeHook(new LoggingHook(
    logger,
    new LoggingConfiguration
    {
        LogLevel = LogLevel.Debug,
        LogArguments = true,
        LogResult = false
    }
));

pipeline.AddPostInvokeHook(new OutputTransformHook(
    new OutputTransformConfiguration
    {
        RedactPatterns = ["password", "secret"],
        RedactedValue = "[REDACTED]"
    }
));

// Execute pre-invoke hooks
await pipeline.ExecutePreInvokeHooksAsync(context);

// Call the tool
var toolResult = await client.CallToolAsync(...);

// Execute post-invoke hooks
var finalResult = await pipeline.ExecutePostInvokeHooksAsync(context, toolResult);
```

### Custom Hooks

```csharp
public class ValidationHook : IPreInvokeHook
{
    public int Priority => 0; // Lower = earlier

    public ValueTask OnPreInvokeAsync(HookContext<CallToolRequestParams> context)
    {
        var args = context.Request.Arguments;
        
        if (args is not null && args.ContainsKey("dangerous_param"))
        {
            // Remove dangerous parameter or throw to reject
            args.Remove("dangerous_param");
        }
        
        return ValueTask.CompletedTask;
    }
}

public class AuditHook : IPostInvokeHook
{
    private readonly IAuditService _auditService;

    public int Priority => 100; // Run after other hooks

    public async ValueTask<CallToolResult> OnPostInvokeAsync(
        HookContext<CallToolRequestParams> context,
        CallToolResult result)
    {
        await _auditService.LogAsync(
            context.ServerName,
            context.ToolName,
            result.IsError);
        
        return result;
    }
}
```

### Hook Factory

```csharp
var factory = new HookFactory(loggerFactory, memoryCache, metrics);

// Register custom hook types
factory.RegisterHookType("validation", (definition, hookFactory) => new ValidationHook());
factory.RegisterHookType("audit", (definition, hookFactory) => new CustomAuditHook(auditService));

// Create hooks from configuration
var pipeline = new HookPipeline(loggerFactory.CreateLogger<HookPipeline>());
factory.ConfigurePipeline(hooksConfiguration, pipeline);
```

## Extension Points

### IToolTransformer

Transform tool definitions before exposing to clients.

```csharp
public interface IToolTransformer
{
    Tool Transform(Tool tool, string serverName);
}

public class CustomTransformer : IToolTransformer
{
    public Tool Transform(Tool tool, string serverName)
    {
        return tool with
        {
            Name = $"custom_{tool.Name}",
            Description = $"[{serverName}] {tool.Description}"
        };
    }
}
```

### IAuthenticationHandler

Custom authentication for HTTP endpoints.

```csharp
public interface IAuthenticationHandler
{
    string SchemeName { get; }
    
    ValueTask<AuthenticationResult> AuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
    
    ValueTask ChallengeAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}

public class CustomAuthHandler : IAuthenticationHandler
{
    public string SchemeName => "Custom";

    public async ValueTask<AuthenticationResult> AuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var token = context.Request.Headers["Authorization"]
            .FirstOrDefault()
            ?.Replace("Bearer ", "");
        
        if (string.IsNullOrEmpty(token))
            return AuthenticationResult.Failure("Missing token");
        
        var isValid = await ValidateTokenAsync(token);
        return isValid 
            ? AuthenticationResult.Success(token) 
            : AuthenticationResult.Failure("Invalid token");
    }

    public ValueTask ChallengeAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = 401;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return ValueTask.CompletedTask;
    }
}
```

### Telemetry

```csharp
// Access metrics
var metrics = serviceProvider.GetRequiredService<ProxyMetrics>();

// Record tool call metrics (separate methods for different aspects)
metrics.RecordToolCall("server", "tool");
metrics.RecordToolCallSuccess("server", "tool");
metrics.RecordToolCallFailure("server", "tool", "TimeoutException");
metrics.RecordToolCallDuration("server", "tool", durationMs: 100);

// Access activity source for custom spans
var activitySource = serviceProvider.GetRequiredService<ProxyActivitySource>();

using var activity = activitySource.StartToolCall("server", "tool");
try
{
    // Do work
    activity?.SetTag("custom.tag", "value");
    ProxyActivitySource.RecordSuccess(activity);
}
catch (Exception ex)
{
    ProxyActivitySource.RecordError(activity, ex);
    throw;
}
```

## ASP.NET Core Integration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add MCP Proxy services
builder.Services.AddSingleton<McpClientManager>();
builder.Services.AddSingleton<McpProxyServer>();
builder.Services.AddSingleton<HookFactory>();
// Note: FilterFactory is a static class, no registration needed

// Add telemetry
builder.Services.AddProxyTelemetry(config.Proxy?.Telemetry);

var app = builder.Build();

// Initialize backend connections
var clientManager = app.Services.GetRequiredService<McpClientManager>();
await clientManager.InitializeAsync(config.Mcp, app.Lifetime.ApplicationStopping);

// Add MCP server middleware
app.UseMcpServer();

await app.RunAsync();
```
