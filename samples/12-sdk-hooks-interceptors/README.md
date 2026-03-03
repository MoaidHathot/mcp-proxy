# SDK Hooks and Interceptors

This example demonstrates how to use the SDK to create custom hooks and interceptors for advanced tool processing.

## Features Demonstrated

- **Pre-Invoke Hooks**: Execute logic before tool calls
- **Post-Invoke Hooks**: Process and modify tool results
- **Tool Interceptors**: Modify the aggregated tool list
- **Tool Call Interceptors**: Intercept and handle tool calls
- **Lambda-based Hooks**: Inline hook definitions
- **Per-Server Hooks**: Hooks that only apply to specific servers

## Hook Pipeline

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         TOOL CALL REQUEST                               │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      TOOL CALL INTERCEPTORS                             │
│  Can short-circuit the call and return a result directly                │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                       GLOBAL PRE-INVOKE HOOKS                           │
│  Logging, authentication, rate limiting, input validation              │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      PER-SERVER PRE-INVOKE HOOKS                        │
│  Server-specific processing                                             │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        BACKEND TOOL EXECUTION                           │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      PER-SERVER POST-INVOKE HOOKS                       │
│  Server-specific result processing                                      │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      GLOBAL POST-INVOKE HOOKS                           │
│  Result transformation, audit logging, metrics                          │
└─────────────────────────────────────────────────────────────────────────┘
```

## Code Examples

### Global Pre-Invoke Hook (Logging)

```csharp
proxy.OnPreInvoke(ctx =>
{
    Console.WriteLine($"[{DateTime.UtcNow:O}] Calling tool: {ctx.Data.Name}");
    Console.WriteLine($"  Server: {ctx.ServerName}");
    Console.WriteLine($"  Arguments: {ctx.Data.Arguments}");

    // Store data for post-invoke
    ctx.Items["startTime"] = Stopwatch.StartNew();

    return ValueTask.CompletedTask;
});
```

### Global Post-Invoke Hook (Timing)

```csharp
proxy.OnPostInvoke((ctx, result) =>
{
    if (ctx.Items.TryGetValue("startTime", out var obj) && obj is Stopwatch sw)
    {
        sw.Stop();
        Console.WriteLine($"[{DateTime.UtcNow:O}] Tool completed in {sw.ElapsedMilliseconds}ms");
    }

    return ValueTask.FromResult(result);
});
```

### Tool Interceptor (Filter/Modify Tool List)

```csharp
proxy.InterceptTools(tools =>
{
    return tools
        .Where(t => !t.Tool.Name.Contains("deprecated"))
        .Select(t =>
        {
            // Add a disclaimer to all tool descriptions
            t.Tool = new Tool
            {
                Name = t.Tool.Name,
                Description = t.Tool.Description + " [Proxied]",
                InputSchema = t.Tool.InputSchema
            };
            return t;
        });
});
```

### Tool Call Interceptor (Handle Calls Directly)

```csharp
proxy.InterceptToolCalls((context, ct) =>
{
    // Handle a specific tool locally
    if (context.ToolName == "proxy_status")
    {
        return ValueTask.FromResult<CallToolResult?>(new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Proxy is healthy!" }]
        });
    }

    // Return null to continue with normal routing
    return ValueTask.FromResult<CallToolResult?>(null);
});
```

### Per-Server Hooks

```csharp
proxy.AddStdioServer("filesystem", "npx", "-y", "@anthropic/mcp-server-filesystem", "/tmp")
    .OnPreInvoke(ctx =>
    {
        // This only runs for filesystem server calls
        Console.WriteLine($"Filesystem operation: {ctx.Data.Name}");
        return ValueTask.CompletedTask;
    })
    .OnPostInvoke((ctx, result) =>
    {
        // Log file operations
        Console.WriteLine($"Filesystem result: {result.IsError}");
        return ValueTask.FromResult(result);
    })
    .Build();
```

## Running the Example

```bash
dotnet build
dotnet run
```

## Use Cases

### Security: Input Validation

```csharp
proxy.OnPreInvoke(ctx =>
{
    // Block SQL injection attempts
    var argsJson = ctx.Data.Arguments?.ToString() ?? "";
    if (argsJson.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Suspicious input detected");
    }
    return ValueTask.CompletedTask;
});
```

### Observability: Metrics Collection

```csharp
proxy.OnPostInvoke((ctx, result) =>
{
    var duration = ((Stopwatch)ctx.Items["timer"]).Elapsed;
    metrics.RecordToolCall(
        toolName: ctx.Data.Name,
        serverName: ctx.ServerName,
        duration: duration,
        success: !result.IsError
    );
    return ValueTask.FromResult(result);
});
```

### Privacy: PII Redaction

```csharp
proxy.OnPostInvoke((ctx, result) =>
{
    foreach (var content in result.Content ?? [])
    {
        if (content is TextContentBlock textBlock)
        {
            textBlock.Text = RedactPII(textBlock.Text);
        }
    }
    return ValueTask.FromResult(result);
});
```

## Next Steps

- See [13-sdk-virtual-tools](../13-sdk-virtual-tools) for creating custom virtual tools
- See [06-hooks](../06-hooks) for JSON-based hook configuration
