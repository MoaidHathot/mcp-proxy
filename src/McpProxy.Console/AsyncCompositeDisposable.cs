namespace McpProxy.Console;

public class AsyncCompositeDisposable : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables;

    public AsyncCompositeDisposable()
    {
        _disposables = new List<IAsyncDisposable>();
    }

    public void Add(IAsyncDisposable disposable)
        => _disposables.Add(disposable);

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }

        _disposables.Clear();
    }
}
