using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using McpProxy.Abstractions;
using McpProxy.Core.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

// Create a host builder for the application
var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Configure MCP Proxy with hooks and interceptors
builder.Services.AddMcpProxy(proxy =>
{
    proxy.WithServerInfo("SDK Hooks Example", "1.0.0");

    // ═══════════════════════════════════════════════════════════════════════
    // GLOBAL PRE-INVOKE HOOKS
    // These run before every tool call across all servers
    // ═══════════════════════════════════════════════════════════════════════

    // Hook 1: Logging (runs first due to low priority)
    proxy.OnPreInvoke(ctx =>
    {
        var sw = Stopwatch.StartNew();
        ctx.Items["stopwatch"] = sw;
        ctx.Items["requestId"] = Guid.NewGuid().ToString("N")[..8];

        Console.WriteLine($"[{ctx.Items["requestId"]}] PRE-INVOKE: {ctx.ServerName}.{ctx.Data.Name}");

        if (ctx.Data.Arguments is not null)
        {
            Console.WriteLine($"[{ctx.Items["requestId"]}]   Args: {ctx.Data.Arguments}");
        }

        return ValueTask.CompletedTask;
    }, priority: -1000);

    // Hook 2: Input validation
    proxy.OnPreInvoke(ctx =>
    {
        var argsJson = ctx.Data.Arguments?.ToString() ?? "";

        // Block potential injection attempts
        var dangerousPatterns = new[] { "DROP TABLE", "DELETE FROM", "; --", "UNION SELECT" };
        foreach (var pattern in dangerousPatterns)
        {
            if (argsJson.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[{ctx.Items["requestId"]}] Blocked: Suspicious input detected");
            }
        }

        return ValueTask.CompletedTask;
    }, priority: -500);

    // ═══════════════════════════════════════════════════════════════════════
    // GLOBAL POST-INVOKE HOOKS
    // These run after every tool call across all servers
    // ═══════════════════════════════════════════════════════════════════════

    // Hook 1: Timing and metrics
    proxy.OnPostInvoke((ctx, result) =>
    {
        if (ctx.Items.TryGetValue("stopwatch", out var obj) && obj is Stopwatch sw)
        {
            sw.Stop();
            Console.WriteLine(
                $"[{ctx.Items["requestId"]}] POST-INVOKE: {ctx.ServerName}.{ctx.Data.Name} " +
                $"completed in {sw.ElapsedMilliseconds}ms (IsError: {result.IsError})");
        }

        return ValueTask.FromResult(result);
    }, priority: 1000);

    // Hook 2: PII Redaction
    proxy.OnPostInvoke((ctx, result) =>
    {
        // Redact email addresses and SSNs from output
        var emailPattern = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
        var ssnPattern = new Regex(@"\b\d{3}-\d{2}-\d{4}\b");

        if (result.Content is not null)
        {
            foreach (var content in result.Content)
            {
                if (content is TextContentBlock textBlock)
                {
                    textBlock.Text = emailPattern.Replace(textBlock.Text, "[EMAIL REDACTED]");
                    textBlock.Text = ssnPattern.Replace(textBlock.Text, "[SSN REDACTED]");
                }
            }
        }

        return ValueTask.FromResult(result);
    }, priority: 500);

    // ═══════════════════════════════════════════════════════════════════════
    // TOOL INTERCEPTORS
    // Modify the aggregated tool list before it's returned to clients
    // ═══════════════════════════════════════════════════════════════════════

    // Interceptor 1: Remove deprecated tools
    proxy.InterceptTools(tools =>
    {
        return tools.Where(t =>
        {
            var isDeprecated = t.Tool.Name.Contains("deprecated") ||
                               (t.Tool.Description?.Contains("[DEPRECATED]") ?? false);

            if (isDeprecated)
            {
                Console.WriteLine($"Filtering out deprecated tool: {t.Tool.Name}");
            }

            return !isDeprecated;
        });
    });

    // Interceptor 2: Add metadata to tool descriptions
    proxy.InterceptTools(tools =>
    {
        return tools.Select(t =>
        {
            // Add server origin to description
            var desc = t.Tool.Description ?? "";
            t.Tool = new Tool
            {
                Name = t.Tool.Name,
                Description = $"{desc} [via {t.ServerName}]",
                InputSchema = t.Tool.InputSchema,
                Annotations = t.Tool.Annotations
            };
            return t;
        });
    });

    // ═══════════════════════════════════════════════════════════════════════
    // TOOL CALL INTERCEPTORS
    // Intercept and optionally handle tool calls before routing
    // ═══════════════════════════════════════════════════════════════════════

    // Interceptor: Handle proxy-specific tools locally
    proxy.InterceptToolCalls((context, ct) =>
    {
        // Handle a "proxy_status" tool locally
        if (context.ToolName == "proxy_status")
        {
            return ValueTask.FromResult<CallToolResult?>(new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(new
                        {
                            status = "healthy",
                            timestamp = DateTime.UtcNow,
                            version = "1.0.0"
                        }, new JsonSerializerOptions { WriteIndented = true })
                    }
                ]
            });
        }

        // Handle a "proxy_echo" tool locally
        if (context.ToolName == "proxy_echo")
        {
            var message = context.Request.Arguments?["message"]?.ToString() ?? "No message";
            return ValueTask.FromResult<CallToolResult?>(new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Echo: {message}" }]
            });
        }

        // Return null to continue with normal routing
        return ValueTask.FromResult<CallToolResult?>(null);
    });

    // ═══════════════════════════════════════════════════════════════════════
    // BACKEND SERVERS WITH PER-SERVER HOOKS
    // ═══════════════════════════════════════════════════════════════════════

    // Filesystem server with server-specific hooks
    proxy.AddStdioServer("filesystem", "npx", "-y", "@anthropic/mcp-server-filesystem", "/tmp")
        .WithTitle("Filesystem")
        .WithToolPrefix("fs")
        .OnPreInvoke(ctx =>
        {
            // Server-specific: Log file paths being accessed
            if (ctx.Data.Arguments?.TryGetPropertyValue("path", out var pathElement) == true)
            {
                Console.WriteLine($"[FILESYSTEM] Accessing path: {pathElement}");
            }
            return ValueTask.CompletedTask;
        })
        .OnPostInvoke((ctx, result) =>
        {
            // Server-specific: Warn about large file reads
            foreach (var content in result.Content ?? [])
            {
                if (content is TextContentBlock { Text.Length: > 10000 })
                {
                    Console.WriteLine($"[FILESYSTEM] Warning: Large content returned ({content.Type})");
                }
            }
            return ValueTask.FromResult(result);
        })
        .FilterTools((tool, _) => !tool.Name.Contains("delete"))  // Extra safety
        .Build();

    // Remote server
    proxy.AddSseServer("context7", "https://mcp.context7.com/sse")
        .WithTitle("Context7")
        .OnPreInvoke(ctx =>
        {
            Console.WriteLine($"[CONTEXT7] Remote call: {ctx.Data.Name}");
            return ValueTask.CompletedTask;
        })
        .Build();
});

// Build and run
var app = builder.Build();

Console.WriteLine("Initializing MCP Proxy with hooks and interceptors...");
await app.Services.InitializeMcpProxyAsync();
Console.WriteLine("MCP Proxy initialized!");

await app.RunAsync();
