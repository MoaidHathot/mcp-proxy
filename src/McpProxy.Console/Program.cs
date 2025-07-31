using System.Text.Json;
using McpProxy.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var mcpClientOptions = new MCPClientOptions();

builder.Configuration.AddEnvironmentVariables();
builder.Configuration.GetSection("Proxy").Bind(mcpClientOptions);
// builder.Services.AddOptions<MCPClientOptions>().Bind(builder.Configuration.GetSection("Proxy")).ValidateOnStart();

var configPath = Environment.GetEnvironmentVariable("MCP_PROXY_CONFIG_PATH")!;

if(builder.Environment.IsDevelopment())
{
    configPath = "mcp-proxy.json";
}

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var jsonConfig = File.ReadAllText(configPath);
var mcpConfig = JsonSerializer.Deserialize<McpProxyConfig>(jsonConfig, options);

mcpClientOptions.Tools = mcpConfig?.Mcp ?? [];

var clients = await CreateMcpClients(mcpClientOptions).ConfigureAwait(false);
// builder.Services.AddSingleton(new MCPClientComposite(clients));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithListToolsHandler(ListProxyTools)
    .WithCallToolHandler(CallProxyTools);

async ValueTask<CallToolResult> CallProxyTools(RequestContext<CallToolRequestParams> context, CancellationToken token)
{
    var clientTools = await Task.WhenAll(
            clients.Select(async p => {
                    var tools = await p.Value.Client.ListToolsAsync().ConfigureAwait(false);
                    return (client: p.Value.Client, tools: tools);
                }
            )).ConfigureAwait(false);

    var pair = clientTools.First(p => p.tools.Any(t => string.Equals(t.Name, context.Params!.Name, StringComparison.InvariantCultureIgnoreCase)));

    var result = await pair.client.CallToolAsync(context.Params!.Name, context.Params.Arguments!.ToDictionary(p => p.Key, p => (object?)p.Value), cancellationToken: token).ConfigureAwait(false);
    return result;
}

async ValueTask<ListToolsResult> ListProxyTools(RequestContext<ListToolsRequestParams> context, CancellationToken token)
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

    foreach(var mcp in options.Tools)
    {
        if(string.Equals(mcp.Value.Type, "stdio", StringComparison.InvariantCultureIgnoreCase))
        {
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = mcp.Key,
                Command = mcp.Value.Command!,
                Arguments = mcp.Value.Arguments,
            });

            var client = await McpClientFactory.CreateAsync(clientTransport).ConfigureAwait(false);
            map.Add(mcp.Key, new McpInfo(mcp.Value, client));
        }
        else if(string.Equals(mcp.Value.Type, "http", StringComparison.InvariantCultureIgnoreCase) || string.Equals(mcp.Value.Type, "sse", StringComparison.InvariantCultureIgnoreCase))
        {
            var clientTransport = new SseClientTransport(new SseClientTransportOptions
            {
                Endpoint = new Uri(mcp.Value.Url!),
                Name = mcp.Key,
            });

            var client = await McpClientFactory.CreateAsync(clientTransport).ConfigureAwait(false);
            map.Add(mcp.Key, new McpInfo(mcp.Value, client));
        }
        else
        {
            throw new NotSupportedException($"Unsupported MCP type: {mcp.Value.Type}");
        }
    }

    return map;
}

await builder.Build().RunAsync().ConfigureAwait(false);
