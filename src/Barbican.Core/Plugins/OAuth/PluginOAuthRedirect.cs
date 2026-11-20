using Barbican.Core.Auth;

namespace Barbican.Core.Plugins.OAuth;

/// <summary>Owns the profile's loopback callback endpoint for add-on OAuth.</summary>
public sealed class PluginOAuthRedirect(PluginOAuthCallbacks callbacks, TimeProvider time) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _deadline;
    private Task? _listener;
    private Task? _closing;
    private Uri? _redirect;

    public Uri? GetOrStart()
    {
        lock (_gate)
        {
            if (_closing is not null) return null;
            if (_listener is { IsCompleted: false }) return RenewActive() ? _redirect : null;

            var started = LoopbackListener.Start(preferredPort: 0);
            if (!started.IsOk || started.Value is not { } endpoint) return null;
            _redirect = endpoint.PluginRedirectUri;
            _deadline?.Dispose();
            _deadline = new CancellationTokenSource(PluginOAuthCallbacks.Lifetime, time);
            var deadlineToken = _deadline.Token;
            _listener = Task.Run(() => RunAsync(endpoint, deadlineToken));
            return _redirect;
        }
    }

    public Uri? Current
    {
        get
        {
            lock (_gate) return IsActive() ? _redirect : null;
        }
    }

    internal bool RenewActive()
    {
        lock (_gate)
        {
            if (!IsActive()) return false;
            _deadline!.CancelAfter(PluginOAuthCallbacks.Lifetime);
            return !_deadline.IsCancellationRequested;
        }
    }

    private bool IsActive() =>
        _closing is null && _listener is { IsCompleted: false } && _deadline is { IsCancellationRequested: false };

    private async Task RunAsync(LoopbackListener endpoint, CancellationToken deadline)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, deadline);
        try { await endpoint.ReceivePluginCallbacksAsync(callbacks, time, stop.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException) { }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_closing ??= Task.Run(CloseAsync));
    }

    private async Task CloseAsync()
    {
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // The loopback worker runs on the thread pool and stops when the token is cancelled.
            if (_listener is { } listener) await listener.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            _deadline?.Dispose();
            _stop.Dispose();
        }
    }
}
