using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithListToolsHandler(ListProxyTools);

async ValueTask<ListToolsResult> ListProxyTools(RequestContext<ListToolsRequestParams> context, CancellationToken token)
{
    await Task.CompletedTask.ConfigureAwait(false);

    return new ListToolsResult
    {
        Tools = new[]
        {
            new Tool
            {
                Name = "Test Tool Name",
                Description = "Test Tool Description",
                Title = "Test Tool Title",
                Annotations = new ToolAnnotations()
                {
                    Title = "Test Tool Title Annotation",
                },
            }
        }
    };
}

await builder.Build().RunAsync().ConfigureAwait(false);
