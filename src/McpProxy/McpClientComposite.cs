using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpProxy.Console;

public class MCPClientComposite : IMcpClient
{
    public readonly IReadOnlyDictionary<string, McpInfo> _clients;

    public MCPClientComposite(IReadOnlyDictionary<string, McpInfo> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients;
    }

    public ServerCapabilities ServerCapabilities
        => _clients.First().Value.Client.ServerCapabilities;

    public Implementation ServerInfo => _clients.First().Value.Client.ServerInfo;

    public string? ServerInstructions => "Test instruction";

    public string? SessionId => null;

    public async ValueTask DisposeAsync()
    {
        var tasks = _clients.Select(p => p.Value.Client.DisposeAsync().AsTask());
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
    {
        var compositeDisposable = new AsyncCompositeDisposable();

        foreach (var client in _clients.Values)
        {
            compositeDisposable.Add(client.Client.RegisterNotificationHandler(method, handler));
        }

        return compositeDisposable;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var client in _clients.Values)
        {
            await client.Client.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        var tasks = _clients.Values.Select(client => client.Client.SendRequestAsync(request, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        //Todo chouls change to only the relevant one
        return results.First();
    }
}
