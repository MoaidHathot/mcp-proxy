# SDK Virtual Tools

This example demonstrates how to create virtual tools that are handled directly by the proxy without forwarding to any backend server.

## Features Demonstrated

- **Virtual Tools**: Proxy-handled tools
- **Tool Modification**: Rename, remove, and modify backend tools
- **Tool Composition**: Combine multiple backend tools into one
- **Dynamic Tool Generation**: Create tools at runtime

## What are Virtual Tools?

Virtual tools are tools that exist only in the proxy layer. When a client calls a virtual tool, the proxy handles it directly instead of forwarding to a backend server.

```
┌────────────────────────────────────────────────────────────────────────┐
│                           MCP CLIENT                                    │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                           MCP PROXY                                     │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      VIRTUAL TOOLS                                │  │
│  │  • proxy_status     - Return proxy health                        │  │
│  │  • combined_search  - Search across multiple backends            │  │
│  │  • cached_query     - Cache-enabled queries                      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                    │                                    │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                      BACKEND TOOLS                                │  │
│  │  filesystem.read_file, github.get_repo, etc.                     │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
```

## Code Examples

### Simple Virtual Tool

```csharp
proxy.AddTool(
    name: "proxy_status",
    description: "Get the current status of the MCP Proxy",
    handler: (request, ct) =>
    {
        return ValueTask.FromResult(new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(new
                    {
                        status = "healthy",
                        uptime = DateTime.UtcNow - startTime,
                        version = "1.0.0"
                    })
                }
            ]
        });
    });
```

### Virtual Tool with Input Schema

```csharp
proxy.AddVirtualTool(
    new Tool
    {
        Name = "calculate",
        Description = "Perform basic arithmetic calculations",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["expression"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Math expression to evaluate (e.g., '2 + 2')"
                }
            },
            ["required"] = new JsonArray { "expression" }
        }
    },
    handler: (request, ct) =>
    {
        var expr = request.Arguments?["expression"]?.ToString() ?? "0";
        // Simple evaluation (use a real math parser in production)
        var result = EvaluateExpression(expr);

        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Result: {result}" }]
        });
    });
```

### Tool Modification

```csharp
// Rename a tool
proxy.RenameTool("filesystem_read_file", "read");

// Remove tools by pattern
proxy.RemoveToolsByPattern("internal_*", "*_debug", "*_test");

// Remove tools by predicate
proxy.RemoveTools((tool, serverName) =>
    tool.Name.StartsWith("admin_") && serverName != "admin-server");

// Modify tool descriptions
proxy.ModifyTools(
    predicate: (tool, _) => tool.Name.StartsWith("fs_"),
    modifier: tool => new Tool
    {
        Name = tool.Name,
        Description = $"[FILESYSTEM] {tool.Description}",
        InputSchema = tool.InputSchema
    });
```

## Use Cases

### 1. Proxy Health Check

```csharp
proxy.AddTool("health", "Check proxy health", (_, _) =>
    ValueTask.FromResult(new CallToolResult
    {
        Content = [new TextContentBlock { Text = "OK" }]
    }));
```

### 2. Combined Search

```csharp
proxy.AddVirtualTool(
    new Tool { Name = "search_all", Description = "Search across all backends" },
    async (request, ct) =>
    {
        var query = request.Arguments?["query"]?.ToString();
        var results = new List<string>();

        // Call multiple backend tools
        results.Add(await SearchGitHub(query, ct));
        results.Add(await SearchFilesystem(query, ct));
        results.Add(await SearchDatabase(query, ct));

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = string.Join("\n\n", results) }]
        };
    });
```

### 3. Caching Wrapper

```csharp
var cache = new MemoryCache(new MemoryCacheOptions());

proxy.AddVirtualTool(
    new Tool { Name = "cached_fetch", Description = "Fetch with caching" },
    async (request, ct) =>
    {
        var key = request.Arguments?["url"]?.ToString();
        if (cache.TryGetValue(key, out var cached))
        {
            return (CallToolResult)cached!;
        }

        var result = await FetchFromBackend(key, ct);
        cache.Set(key, result, TimeSpan.FromMinutes(5));
        return result;
    });
```

### 4. Rate-Limited Tool

```csharp
var rateLimiter = new SemaphoreSlim(5); // Max 5 concurrent calls

proxy.AddVirtualTool(
    new Tool { Name = "limited_api", Description = "Rate-limited API call" },
    async (request, ct) =>
    {
        if (!await rateLimiter.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "Rate limit exceeded" }]
            };
        }

        try
        {
            return await CallBackendApi(request, ct);
        }
        finally
        {
            rateLimiter.Release();
        }
    });
```

## Running the Example

```bash
dotnet build
dotnet run
```

## Next Steps

- See [11-sdk-basic](../11-sdk-basic) for basic SDK usage
- See [12-sdk-hooks-interceptors](../12-sdk-hooks-interceptors) for hooks and interceptors
