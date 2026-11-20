// Flush already-applied preferences before native or hidden-automation close.
// Windows App SDK AppWindow.Closing is cancellable for system affordances; programmatic Close uses CloseAsync directly.
using Barbican.App.Plugins;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Microsoft.Extensions.Logging;

namespace Barbican.App.Shell;

/// <summary>Keeps the window alive until debounced preference writes have reached disk.</summary>
internal sealed partial class WindowCloseController(IPrefsEditor prefs, IDispatcher dispatcher, PluginPackageHost packages, PluginBackgroundRuntimes runtimes,
    ILogger<WindowCloseController> log, IToastService? toasts = null)
{
    internal Func<Task<bool>>? BeforeClose { get; set; }

    private bool _closing;
#if WINDOWS
    private bool _approved;
#endif

#pragma warning disable CA1822 // The Windows implementation captures this controller; native Uno close is deferred to M9.
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

#pragma warning restore CA1822

    public async Task CloseAsync(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_closing) { Trace("already closing"); return; }
        _closing = true;
        Trace("flush start");
        try
        {
            if (BeforeClose is not null && !await BeforeClose().ConfigureAwait(true))
            {
                _closing = false;
                return;
            }

            await prefs.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            Trace("flush complete");
            var draining = runtimes.StopAsync();
            await packages.BeginRuntimeShutdownAsync().ConfigureAwait(false);
            if (!await draining.ConfigureAwait(false))
            {
                dispatcher.Post(() =>
                {
                    _closing = false;
                    toasts?.Show(new ToastRequest("An add-on is still closing. Try closing the window again shortly.", IsError: true));
                });
                return;
            }
            var stopped = await packages.ShutdownAsync().ConfigureAwait(false);
            if (!stopped.IsOk) LogAddOnCleanupIncomplete(log);
            // A synchronous UI post must own Close: do not await a UI continuation after it tears down the dispatcher.
            dispatcher.Post(() =>
            {
                Trace("close on UI");
#if WINDOWS
                _approved = true;
#endif
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

    [LoggerMessage(EventId = 811, Level = LogLevel.Warning, Message = "Add-on teardown was incomplete during window close.")]
    private static partial void LogAddOnCleanupIncomplete(ILogger logger);

    [System.Diagnostics.Conditional("AUTOMATION")]
    private static void Trace(string stage)
    {
        if (Environment.GetEnvironmentVariable("BARBICAN_SHUTDOWN_TRACE") is "1") Console.WriteLine("[shutdown] " + stage);
    }
}
