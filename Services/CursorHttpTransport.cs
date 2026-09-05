using System.Net.Http;

namespace Quota.Services;

/// <summary>
/// Переиспользуемый HTTP transport для Cursor API с атомарной заменой после сетевых сбоев.
/// </summary>
public sealed class CursorHttpTransport : IDisposable
{
    private readonly object _sync = new();
    private readonly TimeSpan _timeout;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private HttpMessageHandler _handler;
    private HttpClient _client;
    private bool _disposed;
    private int _resetCount;

    public CursorHttpTransport(TimeSpan? timeout = null)
        : this(() => new SocketsHttpHandler(), timeout)
    {
    }

    internal CursorHttpTransport(Func<HttpMessageHandler> handlerFactory, TimeSpan? timeout = null)
    {
        _handlerFactory = handlerFactory;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        (_handler, _client) = CreateClient();
    }

    internal int ResetCount => Volatile.Read(ref _resetCount);

    public HttpClient GetClient()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _client;
        }
    }

    public void Reset()
    {
        HttpClient? oldClient = null;

        lock (_sync)
        {
            if (_disposed)
                return;

            oldClient = _client;
            (_handler, _client) = CreateClient();
            Interlocked.Increment(ref _resetCount);
        }

        oldClient?.Dispose();
    }

    public void Dispose()
    {
        HttpClient? client;
        HttpMessageHandler? handler;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            client = _client;
            handler = _handler;
            _client = null!;
            _handler = null!;
        }

        client?.Dispose();
        if (handler is IDisposable disposable)
            disposable.Dispose();
    }

    private (HttpMessageHandler Handler, HttpClient Client) CreateClient()
    {
        var handler = _handlerFactory();
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = _timeout
        };
        return (handler, client);
    }
}
