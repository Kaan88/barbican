// Flush already-applied preferences before native or hidden-automation close.
// Windows App SDK AppWindow.Closing is cancellable for system affordances; programmatic Close uses CloseAsync directly.
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.App.Shell;

/// <summary>Keeps the window alive until debounced preference writes have reached disk.</summary>
public sealed class WindowCloseController(IPrefsEditor prefs, IDispatcher dispatcher, IToastService? toasts = null)
{
    private bool _closing;
    private bool _approved;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
#if AUTOMATION
        window.Closed += (_, _) => Trace("closed");
#endif
#if WINDOWS
        window.AppWindow.Closing += (sender, args) =>
        {
            if (_approved) { args.Cancel = false; return; }
            args.Cancel = true;
            _ = CloseAsync(window);
        };
#endif
    }

    public async Task CloseAsync(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_closing) { Trace("already closing"); return; }
        _closing = true;
        Trace("flush start");
        try
        {
            await prefs.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            Trace("flush complete");
            // A synchronous UI post must own Close: do not await a UI continuation after it tears down the dispatcher.
            dispatcher.Post(() =>
            {
                Trace("close on UI");
                _approved = true;
                window.Close();
                Trace("close returned");
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace("flush failed");
            dispatcher.Post(() =>
            {
                _closing = false;
                toasts?.Show(new ToastRequest("Settings could not be saved. The window remains open; retry closing after checking the profile folder.", IsError: true));
            });
        }
    }

    [System.Diagnostics.Conditional("AUTOMATION")]
    private static void Trace(string stage)
    {
        if (Environment.GetEnvironmentVariable("BARBICAN_SHUTDOWN_TRACE") is "1") Console.WriteLine("[shutdown] " + stage);
    }
}
