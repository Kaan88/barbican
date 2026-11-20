#if WINDOWS
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Connects a dedicated WebView's native events to a single runtime binding.</summary>
internal sealed partial class PluginSandboxWindowsEvents : IDisposable
{
    private const int MaxWireBytes = 40 * 1024 * 1024;
    private readonly CoreWebView2 _core;
    private readonly PluginSandboxSession _session;
    private readonly PluginSandboxNavigationGate _navigation;
    private readonly ILogger _log;
    private bool _disposed;

    public PluginSandboxWindowsEvents(PluginSandboxSession session, PluginSandboxResourcePolicy policy, ILogger log)
    {
        _core = session.View.RootIdentity as CoreWebView2
            ?? throw new InvalidOperationException("The plugin session must own an initialized WebView2 core.");
        _session = session;
        _navigation = new PluginSandboxNavigationGate(policy, session.Binding.Kind);
        _log = log;

        // The top-level and frame web-message events are separate. Never subscribe to the latter.
        _core.WebMessageReceived += OnWebMessageReceived;
        _core.NavigationStarting += OnNavigationStarting;
        _core.FrameNavigationStarting += OnFrameNavigationStarting;
        _core.NavigationCompleted += OnNavigationCompleted;
        _core.FrameNavigationCompleted += OnNavigationCompleted;
        _core.SourceChanged += OnSourceChanged;
        _core.ProcessFailed += OnProcessFailed;
        _core.NewWindowRequested += OnNewWindowRequested;
        _core.PermissionRequested += OnPermissionRequested;
        _core.DownloadStarting += OnDownloadStarting;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _core) ||
            !string.Equals(args.Source, _session.View.TrustedRoot.OriginalString, StringComparison.Ordinal))
            return;

        _ = ReceiveAsync(sender, args);
    }

    private async Task ReceiveAsync(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            await _session.ReceiveAsync(sender, true, _session.View.TrustedRoot, () => ReadWire(args),
                _session.Binding.Lifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_session.Binding.IsAlive) { }
#pragma warning disable CA1031 // An event from a failed renderer must never take down the mail window.
        catch (Exception)
#pragma warning restore CA1031
        {
            _ = CloseAsync();
        }
    }

    private static ReadOnlyMemory<byte> ReadWire(CoreWebView2WebMessageReceivedEventArgs args)
    {
        var message = args.TryGetWebMessageAsString();
        if (message is null || message.Length > MaxWireBytes || Encoding.UTF8.GetByteCount(message) > MaxWireBytes)
            throw new FormatException("Invalid plugin message size.");
        return Encoding.UTF8.GetBytes(message);
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args) =>
        CheckNavigation(args, topLevel: true);

    private void OnFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args) =>
        CheckNavigation(args, topLevel: false);

    private void CheckNavigation(CoreWebView2NavigationStartingEventArgs args, bool topLevel)
    {
        if (!_disposed && _session.Binding.IsAlive && !args.IsRedirected && _navigation.TryStart(args.Uri, topLevel))
            return;

        args.Cancel = true;
        _ = CloseAsync();
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess) _ = CloseAsync();
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        if (!string.Equals(sender.Source, _session.View.TrustedRoot.OriginalString, StringComparison.Ordinal))
            _ = CloseAsync();
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args) => _ = CloseAsync();

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args) => args.Handled = true;

    private void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        args.State = CoreWebView2PermissionState.Deny;
        args.Handled = true;
    }

    private void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Cancel = true;
        args.Handled = true;
    }

    private async Task CloseAsync()
    {
        try
        {
            // Invalidation executes before the first await, including during NavigationStarting.
            await _session.NavigationStartingAsync(_session.View.RootIdentity, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The binding is already closed. Report native failure without throwing through WebView.
        catch (Exception)
#pragma warning restore CA1031
        {
            LogCleanupFailed(_log);
        }
    }

    [LoggerMessage(EventId = 810, Level = LogLevel.Warning, Message = "Could not close the isolated plugin WebView.")]
    private static partial void LogCleanupFailed(ILogger logger);

    public void Dispose()
    {
        if (_disposed) return;
        if (_session.Binding.IsAlive)
            throw new InvalidOperationException("Close the plugin binding before detaching its WebView guards.");
        _disposed = true;
        _core.WebMessageReceived -= OnWebMessageReceived;
        _core.NavigationStarting -= OnNavigationStarting;
        _core.FrameNavigationStarting -= OnFrameNavigationStarting;
        _core.NavigationCompleted -= OnNavigationCompleted;
        _core.FrameNavigationCompleted -= OnNavigationCompleted;
        _core.SourceChanged -= OnSourceChanged;
        _core.ProcessFailed -= OnProcessFailed;
        _core.NewWindowRequested -= OnNewWindowRequested;
        _core.PermissionRequested -= OnPermissionRequested;
        _core.DownloadStarting -= OnDownloadStarting;
    }
}
#endif
