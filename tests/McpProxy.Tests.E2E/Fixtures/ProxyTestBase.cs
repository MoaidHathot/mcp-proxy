using McpProxy.Sdk.Configuration;
using McpProxy.Sdk.Hooks;
using McpProxy.Sdk.Proxy;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Text.Json;

#pragma warning disable CA2007 // Consider calling ConfigureAwait on awaited task (test code)
#pragma warning disable CA2213 // Disposable fields should be disposed (mocked fields don't need disposal)

namespace McpProxy.Tests.E2E.Fixtures;

/// <summary>
/// Base class for E2E tests that provides mock MCP client setup and proxy configuration.
/// </summary>
public abstract class ProxyTestBase : IAsyncDisposable
{
    protected readonly ILogger<McpProxyServer> ProxyLogger;
    protected readonly ILogger<McpClientManager> ClientManagerLogger;
    protected readonly ILoggerFactory LoggerFactory;
    protected readonly ILogger<HookPipeline> HookPipelineLogger;
    protected readonly McpClientManager ClientManager;
    protected McpProxyServer? ProxyServer;

    protected ProxyTestBase()
    {
        ProxyLogger = Substitute.For<ILogger<McpProxyServer>>();
        ClientManagerLogger = Substitute.For<ILogger<McpClientManager>>();
        LoggerFactory = Substitute.For<ILoggerFactory>();
        HookPipelineLogger = Substitute.For<ILogger<HookPipeline>>();
        ClientManager = new McpClientManager(ClientManagerLogger, LoggerFactory);
    }

    /// <summary>
    /// Creates a mock MCP client wrapper with the specified tools, resources, and prompts.
    /// </summary>
    protected IMcpClientWrapper CreateMockClient(
        string serverName,
        IList<Tool>? tools = null,
        IList<Resource>? resources = null,
        IList<Prompt>? prompts = null)
    {
        var client = Substitute.For<IMcpClientWrapper>();

        // Setup tool listing
        client.ListToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Tool>>(tools ?? []));

        // Setup tool calls
        client.CallToolAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var toolName = callInfo.ArgAt<string>(0);
                return Task.FromResult(new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Result from {serverName}:{toolName}" }]
                });
            });

        // Setup resource listing
        client.ListResourcesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Resource>>(resources ?? []));

        // Setup resource reading
        client.ReadResourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var uri = callInfo.ArgAt<string>(0);
                return Task.FromResult(new ReadResourceResult
                {
                    Contents = [new TextResourceContents { Uri = uri, Text = $"Content from {serverName}:{uri}" }]
                });
            });

        // Setup prompt listing
        client.ListPromptsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Prompt>>(prompts ?? []));

        // Setup prompt getting
        client.GetPromptAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var promptName = callInfo.ArgAt<string>(0);
                return Task.FromResult(new GetPromptResult
                {
                    Description = $"Prompt from {serverName}",
                    Messages = [new PromptMessage
                    {
                        Role = Role.User,
                        Content = new TextContentBlock { Text = $"Template from {serverName}:{promptName}" }
                    }]
                });
            });

        return client;
    }

    /// <summary>
    /// Creates a Tool protocol object for testing.
    /// </summary>
    protected static Tool CreateTool(string name, string? description = null, string? title = null)
    {
        return new Tool
        {
            Name = name,
            Title = title,
            Description = description ?? $"Description for {name}",
            InputSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement
        };
    }

    /// <summary>
    /// Creates a Resource protocol object for testing.
    /// </summary>
    protected static Resource CreateResource(string uri, string name, string? mimeType = null, string? description = null)
    {
        return new Resource
        {
            Uri = uri,
            Name = name,
            MimeType = mimeType ?? "text/plain",
            Description = description
        };
    }

    /// <summary>
    /// Creates a Prompt protocol object for testing.
    /// </summary>
    protected static Prompt CreatePrompt(string name, string? description = null)
    {
        return new Prompt
        {
            Name = name,
            Description = description ?? $"Description for {name}"
        };
    }

    /// <summary>
    /// Registers a mock client with the client manager.
    /// </summary>
    protected void RegisterClient(string serverName, IMcpClientWrapper client, ServerConfiguration? config = null)
    {
        var serverConfig = config ?? new ServerConfiguration
        {
            Type = ServerTransportType.Stdio,
            Command = "mock"
        };

        ClientManager.RegisterClient(serverName, client, serverConfig);
    }

    /// <summary>
    /// Creates and returns a proxy server with the given configuration.
    /// </summary>
    protected McpProxyServer CreateProxyServer(ProxyConfiguration? config = null)
    {
        var proxyConfig = config ?? new ProxyConfiguration();
        ProxyServer = new McpProxyServer(ProxyLogger, ClientManager, proxyConfig);
        return ProxyServer;
    }

    public async ValueTask DisposeAsync()
    {
        await ClientManager.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
