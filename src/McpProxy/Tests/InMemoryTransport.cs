using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProxy.Console;

public class InMemoryTransport : ITransport, IClientTransport
{
    private readonly Channel<JsonRpcMessage> _channel = Channel.CreateUnbounded<JsonRpcMessage>();

    public bool IsConnected => true;
    public string? SessionId => null;

    public ChannelReader<JsonRpcMessage> MessageReader => _channel.Reader;

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ITransport>(this);

    public ValueTask DisposeAsync() => default;

    public string Name {get; } = "InMemoryTransport";

    public virtual Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case JsonRpcRequest:
                _channel.Writer.TryWrite(new JsonRpcResponse
                        {
                        Id = ((JsonRpcRequest)message).Id,
                        Result = JsonSerializer.SerializeToNode(new InitializeResult
                                {
                                Capabilities = new ServerCapabilities(),
                                ProtocolVersion = "2024-11-05",
                                ServerInfo = new Implementation
                                {
                                Name = "NopTransport",
                                Version = "1.0.0"
                                },
                                }, McpJsonUtilities.DefaultOptions),
                        });
                break;
        }

        return Task.CompletedTask;
    }
}
