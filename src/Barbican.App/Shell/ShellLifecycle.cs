// Mirrors: Bulwark lib/plugin-hook-catalog.ts app group (onAppReady :311, onInterval :319, onWindowFocus/Blur, onVisibilityChange, onOnline/onOffline, onBeforeUnload) and
// PLUGIN_INTERVAL_MS (one minute); the ViewModel (ShellViewModel.Notify*) fires the hooks, this reports what the window and the network did.

using System.Net.NetworkInformation;
using Barbican.Presentation.Shell;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Shell;

/// <summary>Tells the shell ViewModel about the window and the network: ready, the minute tick, focus, visibility, online and closing.</summary>
public sealed class ShellLifecycle : IDisposable
{
    private readonly ShellViewModel _shell;
    private DispatcherQueueTimer? _timer;
    private DispatcherQueue? _queue;
    private bool _started;

    public ShellLifecycle(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <summary>Starts the minute tick and the network watch, and reports the shell as ready. Call once, on the UI thread, when the page is loaded.</summary>
    public void Start(DispatcherQueue queue, string? accountKey)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (_started)
        {
            return;
        }

        _started = true;
        _queue = queue;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += (_, _) => _shell.NotifyInterval();
        _timer.Start();
        NetworkChange.NetworkAvailabilityChanged += OnNetwork;
        _shell.NotifyReady(accountKey);
    }

    /// <summary>Wires the window's activation, visibility and closing; the window belongs to App.xaml.cs, which calls this once after it creates it.</summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Activated += (_, e) => _shell.NotifyWindowActive(!string.Equals(e.WindowActivationState.ToString(), "Deactivated", StringComparison.Ordinal));
        window.VisibilityChanged += (_, e) => _shell.NotifyVisibility(e.Visible);
        window.Closed += (_, _) => _shell.NotifyBeforeClose();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetwork;
        _timer?.Stop();
    }

    private void OnNetwork(object? sender, NetworkAvailabilityEventArgs e) => _ = _queue?.TryEnqueue(() => _shell.NotifyOnline(e.IsAvailable));
}
