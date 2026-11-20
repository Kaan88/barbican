#if WINDOWS
using Barbican.App.Shell;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Owns a native plugin control and removes it on the UI thread after revocation.</summary>
internal sealed class PluginSandboxWindowsView(WebView2 view, Panel mount, PluginSandboxTemporaryProfile temporaryProfile) : IPluginSandboxWebView
{
    private readonly DispatcherQueueDispatcher _ui = new(view.DispatcherQueue);
    private readonly PluginSandboxWindowsSender _sender = new(view);
    private readonly Lock _closeGate = new();
    private Task? _closing;
    private IDisposable? _guards;
    private PluginSandboxWindowsResources? _resources;

    public object RootIdentity { get; } = view.CoreWebView2
        ?? throw new ArgumentException("The isolated WebView must be initialized.", nameof(view));

    public Uri TrustedRoot { get; } = new("https://plugin.invalid/host/index.html");

    /// <summary>Consumes the prepared resources before attaching any native handlers.</summary>
    public Task AttachGuardsAsync(PluginSandboxWindowsResources resources, Func<IDisposable> attach, CancellationToken ct)
    {
        lock (_closeGate)
        {
            if (_closing is not null || _resources is not null)
            {
                if (!ReferenceEquals(_resources, resources)) resources.Dispose();
                throw new InvalidOperationException("The plugin WebView cannot own resources in its current state.");
            }
            _resources = resources;
        }

        return _ui.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            lock (_closeGate)
            {
                if (_closing is not null || _guards is not null)
                    throw new InvalidOperationException("The plugin WebView cannot attach guards in its current state.");
                _guards = attach();
            }
            return Task.CompletedTask;
        });
    }

    public Task NavigateAsync(Uri uri, CancellationToken ct) =>
        _ui.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            lock (_closeGate)
            {
                if (_closing is not null || _guards is null || !string.Equals(uri.OriginalString, TrustedRoot.OriginalString, StringComparison.Ordinal))
                    throw new InvalidOperationException("The plugin WebView cannot navigate to that document.");
                view.CoreWebView2.Navigate(TrustedRoot.AbsoluteUri);
            }
            return Task.CompletedTask;
        });

    public Task<bool> PostAsync(Func<Action<string>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct) =>
        _sender.PostAsync(deliver, ct);

    public ValueTask DisposeAsync()
    {
        lock (_closeGate) return new(_closing ??= CloseAsync());
    }

    private async Task CloseAsync()
    {
        try { await QueueCloseAsync().ConfigureAwait(false); }
        finally { await temporaryProfile.DisposeAsync().ConfigureAwait(false); }
    }

    private Task QueueCloseAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Navigation or renderer failure can request close from inside a native event.
        // Let that event unwind before detaching or closing its sender.
        if (!view.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                CloseOnUi();
                done.SetResult();
            }
#pragma warning disable CA1031 // Propagate native cleanup errors to the owner awaiting disposal.
            catch (Exception error)
#pragma warning restore CA1031
            {
                done.SetException(error);
            }
        }))
        {
            done.SetException(new InvalidOperationException("The plugin WebView's UI queue is closed."));
        }
        return done.Task;
    }

    private void CloseOnUi()
    {
        try
        {
            _guards?.Dispose();
        }
        finally
        {
            try
            {
                view.Close();
                temporaryProfile.MarkBrowserClosed();
                _resources?.Dispose();
            }
            finally { mount.Children.Remove(view); }
        }
    }
}
#endif
