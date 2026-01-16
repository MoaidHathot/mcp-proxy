using System.Text.Json;
using McpProxy.Console;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Parse command-line arguments
var config = ParseArguments(args);

if (config.TransportType == ServerTransportType.Stdio)
{
    await RunStdioServer(args, config).ConfigureAwait(false);
}
else
{
    await RunSseServer(args, config).ConfigureAwait(false);
}

async Task RunStdioServer(string[] args, ProxyConfig proxyConfig)
{
    var builder = Host.CreateApplicationBuilder(args);
    var clients = await SetupMcpClients(proxyConfig.ConfigPath, builder.Environment).ConfigureAwait(false);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithListToolsHandler((context, token) => ListProxyTools(clients, context, token))
        .WithCallToolHandler((context, token) => CallProxyTools(clients, context, token));

    await builder.Build().RunAsync().ConfigureAwait(false);
}

async Task RunSseServer(string[] args, ProxyConfig proxyConfig)
{
    var builder = WebApplication.CreateBuilder(args);
    var clients = await SetupMcpClients(proxyConfig.ConfigPath, builder.Environment).ConfigureAwait(false);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithListToolsHandler((context, token) => ListProxyTools(clients, context, token))
        .WithCallToolHandler((context, token) => CallProxyTools(clients, context, token));

    var app = builder.Build();

    app.MapMcp();

    // System.Console.WriteLine($"MCP Proxy SSE server started");

    await app.RunAsync().ConfigureAwait(false);
}

async Task<IReadOnlyDictionary<string, McpInfo>> SetupMcpClients(string? configPath, IHostEnvironment environment)
{
    var mcpClientOptions = new MCPClientOptions();

    configPath ??= Environment.GetEnvironmentVariable("MCP_PROXY_CONFIG_PATH");

    if (string.IsNullOrEmpty(configPath) && environment.IsDevelopment())
    {
        configPath = "mcp-proxy.json";
    }

    if (string.IsNullOrEmpty(configPath))
    {
        throw new InvalidOperationException("Config path not provided. Use second argument or set MCP_PROXY_CONFIG_PATH environment variable.");
    }

    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    var jsonConfig = File.ReadAllText(configPath);
    var mcpConfig = JsonSerializer.Deserialize<McpProxyConfig>(jsonConfig, options);

    mcpClientOptions.Tools = mcpConfig?.Mcp ?? [];

    return await CreateMcpClients(mcpClientOptions).ConfigureAwait(false);
}

ProxyConfig ParseArguments(string[] args)
{
    if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
    {
        PrintHelp();
        Environment.Exit(0);
    }

    var transportType = args[0].ToLowerInvariant() switch
    {
        "stdio" => ServerTransportType.Stdio,
        "sse" => ServerTransportType.Sse,
        _ => throw new ArgumentException($"Invalid transport type: {args[0]}. Valid values are 'stdio' or 'sse'.")
    };

    var configPath = args.Length > 1 ? args[1] : null;

    return new ProxyConfig
    {
        TransportType = transportType,
        ConfigPath = configPath
    };
}

void PrintHelp()
{
    System.Console.WriteLine("""
        MCP Proxy - A proxy server for Model Context Protocol

        Usage: McpProxy <transport> [config-path]

        Arguments:
          transport    Server transport type: 'stdio' or 'sse'
          config-path  Path to mcp-proxy.json (optional, uses MCP_PROXY_CONFIG_PATH env var if not provided)

        Environment Variables:
          MCP_PROXY_CONFIG_PATH   Path to the mcp-proxy.json configuration file

        Examples:
          McpProxy stdio                      # Run with stdio transport
          McpProxy sse                        # Run with SSE transport
          McpProxy stdio ./mcp-proxy.json     # Run with stdio and specific config
          McpProxy sse /path/to/config.json   # Run with SSE and specific config
        """);
}

async ValueTask<CallToolResult> CallProxyTools(
    IReadOnlyDictionary<string, McpInfo> clients,
    RequestContext<CallToolRequestParams> context,
    CancellationToken token)
{
    var clientTools = await Task.WhenAll(
            clients.Select(async p =>
            {
                var tools = await p.Value.Client.ListToolsAsync().ConfigureAwait(false);
                return (client: p.Value.Client, tools: tools);
            }
            )).ConfigureAwait(false);

    var pair = clientTools.First(p => p.tools.Any(t => string.Equals(t.Name, context.Params!.Name, StringComparison.InvariantCultureIgnoreCase)));

    var result = await pair.client.CallToolAsync(context.Params!.Name, context.Params.Arguments!.ToDictionary(p => p.Key, p => (object?)p.Value), cancellationToken: token).ConfigureAwait(false);
    return result;
}

async ValueTask<ListToolsResult> ListProxyTools(
    IReadOnlyDictionary<string, McpInfo> clients,
    RequestContext<ListToolsRequestParams> context,
    CancellationToken token)
{
    var clientTools = await Task.WhenAll(clients.Select(p => p.Value.Client.ListToolsAsync().AsTask())).ConfigureAwait(false);
    var tools = clientTools.SelectMany(t => t.Select(tool => new Tool
    {
        Name = tool.Name,
        Title = tool.Title,
        Description = tool.Description,
        InputSchema = tool.JsonSchema,
        OutputSchema = tool.ReturnJsonSchema,
    })).ToArray();

    var result = new ListToolsResult
    {
        Tools = tools,
    };

    return result;
}

async Task<IReadOnlyDictionary<string, McpInfo>> CreateMcpClients(MCPClientOptions options)
{
    var map = new Dictionary<string, McpInfo>();

    foreach (var mcp in options.Tools)
    {
        if (string.Equals(mcp.Value.Type, "stdio", StringComparison.InvariantCultureIgnoreCase))
        {
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = mcp.Key,
                Command = mcp.Value.Command!,
                Arguments = mcp.Value.Arguments,
            });

            var client = await McpClient.CreateAsync(clientTransport).ConfigureAwait(false);
            map.Add(mcp.Key, new McpInfo(mcp.Value, client));
        }
        else if (string.Equals(mcp.Value.Type, "http", StringComparison.InvariantCultureIgnoreCase) || string.Equals(mcp.Value.Type, "sse", StringComparison.InvariantCultureIgnoreCase))
        {
            // Use SSE mode for "sse" type, AutoDetect (which tries StreamableHttp first) for "http"
            var transportMode = string.Equals(mcp.Value.Type, "sse", StringComparison.InvariantCultureIgnoreCase)
                ? HttpTransportMode.Sse
                : HttpTransportMode.AutoDetect;

            var clientTransport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(mcp.Value.Url!),
                Name = mcp.Key,
                TransportMode = transportMode,
            });

            var client = await McpClient.CreateAsync(clientTransport).ConfigureAwait(false);
            map.Add(mcp.Key, new McpInfo(mcp.Value, client));
        }
        else
        {
            throw new NotSupportedException($"Unsupported MCP type: {mcp.Value.Type}");
        }
    }

    return map;
}

enum ServerTransportType
{
    Stdio,
    Sse
}

class ProxyConfig
{
    public ServerTransportType TransportType { get; set; } = ServerTransportType.Stdio;
    public string? ConfigPath { get; set; }
}
