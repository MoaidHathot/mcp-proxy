using System.Text.Json;
using System.Text.Json.Nodes;
using McpProxy.Sdk.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var startTime = DateTime.UtcNow;

// Create a host builder for the application
var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure MCP Proxy with virtual tools
builder.Services.AddMcpProxy(proxy =>
{
    proxy.WithServerInfo("SDK Virtual Tools Example", "1.0.0");

    // ═══════════════════════════════════════════════════════════════════════
    // VIRTUAL TOOLS - Simple inline definition
    // ═══════════════════════════════════════════════════════════════════════

    // Simple tool using AddTool helper
    proxy.AddTool(
        name: "proxy_status",
        description: "Get the current status of the MCP Proxy server",
        handler: (request, ct) =>
        {
            var status = new
            {
                status = "healthy",
                uptime = (DateTime.UtcNow - startTime).ToString(),
                version = "1.0.0",
                timestamp = DateTime.UtcNow.ToString("O")
            };

            return ValueTask.FromResult(new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(status, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        })
                    }
                ]
            });
        });

    // Echo tool - demonstrates reading arguments
    proxy.AddTool(
        name: "echo",
        description: "Echo back the provided message",
        handler: (request, ct) =>
        {
            var message = request.Arguments?["message"]?.ToString() ?? "No message provided";

            return ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Echo: {message}" }]
            });
        });

    // ═══════════════════════════════════════════════════════════════════════
    // VIRTUAL TOOLS - With input schema
    // ═══════════════════════════════════════════════════════════════════════

    // Calculator tool with full schema
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
                    ["operation"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "add", "subtract", "multiply", "divide" },
                        ["description"] = "The arithmetic operation to perform"
                    },
                    ["a"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["description"] = "First operand"
                    },
                    ["b"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["description"] = "Second operand"
                    }
                },
                ["required"] = new JsonArray { "operation", "a", "b" }
            }
        },
        handler: (request, ct) =>
        {
            var operation = request.Arguments?["operation"]?.ToString();
            var a = request.Arguments?["a"]?.GetValue<double>() ?? 0;
            var b = request.Arguments?["b"]?.GetValue<double>() ?? 0;

            double result = operation switch
            {
                "add" => a + b,
                "subtract" => a - b,
                "multiply" => a * b,
                "divide" when b != 0 => a / b,
                "divide" => double.NaN,
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            return ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"{a} {operation} {b} = {result}" }]
            });
        });

    // Environment info tool
    proxy.AddVirtualTool(
        new Tool
        {
            Name = "env_info",
            Description = "Get information about the proxy environment",
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["include_env_vars"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Whether to include environment variable names",
                        ["default"] = JsonValue.Create(false)
                    }
                }
            }
        },
        handler: (request, ct) =>
        {
            var includeEnvVars = request.Arguments?["include_env_vars"]?.GetValue<bool>() ?? false;

            var info = new Dictionary<string, object>
            {
                ["os"] = Environment.OSVersion.ToString(),
                ["runtime"] = Environment.Version.ToString(),
                ["machine_name"] = Environment.MachineName,
                ["processor_count"] = Environment.ProcessorCount,
                ["working_directory"] = Environment.CurrentDirectory
            };

            if (includeEnvVars)
            {
                info["env_var_count"] = Environment.GetEnvironmentVariables().Count;
                info["env_vars"] = Environment.GetEnvironmentVariables()
                    .Keys.Cast<string>()
                    .OrderBy(k => k)
                    .Take(10)
                    .ToList();
            }

            return ValueTask.FromResult(new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(info, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        })
                    }
                ]
            });
        });

    // ═══════════════════════════════════════════════════════════════════════
    // TOOL MODIFICATION - Rename, remove, and modify backend tools
    // ═══════════════════════════════════════════════════════════════════════

    // Rename a tool from one of our backends
    proxy.RenameTool("fs_read_file", "read");

    // Remove tools by pattern
    proxy.RemoveToolsByPattern(
        "internal_*",      // Remove internal tools
        "*_debug",         // Remove debug tools
        "*_deprecated"     // Remove deprecated tools
    );

    // Remove tools by predicate
    proxy.RemoveTools((tool, serverName) =>
    {
        // Remove any tool that has "dangerous" in its description
        return tool.Description?.Contains("dangerous", StringComparison.OrdinalIgnoreCase) ?? false;
    });

    // Modify tool descriptions to add server info
    proxy.ModifyTools(
        predicate: (tool, serverName) => serverName == "filesystem",
        modifier: tool => new Tool
        {
            Name = tool.Name,
            Description = $"[Filesystem] {tool.Description}",
            InputSchema = tool.InputSchema,
            Annotations = tool.Annotations
        });

    // ═══════════════════════════════════════════════════════════════════════
    // BACKEND SERVERS
    // ═══════════════════════════════════════════════════════════════════════

    // Add a filesystem backend (tools will be modified by the rules above)
    proxy.AddStdioServer("filesystem", "npx", "-y", "@anthropic/mcp-server-filesystem", "/tmp")
        .WithTitle("Filesystem")
        .WithToolPrefix("fs")
        .Build();

    // Add a remote server
    proxy.AddSseServer("context7", "https://mcp.context7.com/sse")
        .WithTitle("Context7")
        .Build();
});

// Build and run
var app = builder.Build();

Console.WriteLine("Initializing MCP Proxy with virtual tools...");
await app.Services.InitializeMcpProxyAsync();
Console.WriteLine("MCP Proxy initialized!");
Console.WriteLine();
Console.WriteLine("Virtual tools available:");
Console.WriteLine("  - proxy_status: Get proxy health status");
Console.WriteLine("  - echo: Echo back a message");
Console.WriteLine("  - calculate: Perform arithmetic calculations");
Console.WriteLine("  - env_info: Get environment information");
Console.WriteLine();

await app.RunAsync();
