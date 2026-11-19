#if WINDOWS
using System.Text;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>The Windows outbound transport for an already initialized, trusted parent control.</summary>
internal sealed class PluginSandboxWindowsSender(WebView2 view)
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _queue = view.DispatcherQueue;

    public async Task<bool> PostAsync(Func<Action<ReadOnlyMemory<byte>>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        ct.ThrowIfCancellationRequested();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryEnqueue(() => _ = RunAsync()))
        {
            return false;
        }

        return await done.Task.WaitAsync(ct).ConfigureAwait(false);

        async Task RunAsync()
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                done.TrySetResult(await deliver(Write, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                done.TrySetCanceled(ct);
            }
#pragma warning disable CA1031 // Marshal delivery failure back to the awaiting host, not to the UI queue.
            catch (Exception error)
#pragma warning restore CA1031
            {
                done.TrySetException(error);
            }
        }

        void Write(ReadOnlyMemory<byte> message)
        {
            ct.ThrowIfCancellationRequested();
            if (!_queue.HasThreadAccess || view.CoreWebView2 is not { } core)
            {
                throw new InvalidOperationException("Plugin response requires an initialized WebView on its own UI queue.");
            }

            var text = Encoding.UTF8.GetString(message.Span);
            ct.ThrowIfCancellationRequested();
            core.PostWebMessageAsString(text);
        }
    }
}
#endif
