// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2118-2127,2310-2349 (the host side of the ui navigation, refresh and download methods), open-email.ts, calendar-navigation.ts, open-task.ts, row-badges.ts.
// The seams are the app's navigator, view refresh and file saver; a seam the app lacks makes its methods Unavailable. The add-on never gets a service, only the methods.
using Barbican.Core.Plugins.Api.Mail;

namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>The services the navigation add-on methods run on, and the state they share (one save dialog at a time).</summary>
public sealed class PluginNavigationHost(IPluginMailReaders readers, IPluginMailRateLimiter limiter, IAppNavigator? navigator = null, IPluginViewRefresh? refresh = null, IPluginFileSaver? saver = null, Barbican.Core.Plugins.Pages.PluginPageRegistry? pages = null, IPluginPageOpener? pageOpener = null)
{
    private int _saving;

    internal IPluginMailReaders Readers { get; } = readers ?? throw new ArgumentNullException(nameof(readers));

    internal IPluginMailRateLimiter Limiter { get; } = limiter ?? throw new ArgumentNullException(nameof(limiter));

    internal IAppNavigator? Navigator { get; } = navigator;

    internal IPluginViewRefresh? Refresh { get; } = refresh;

    internal IPluginFileSaver? Saver { get; } = saver;

    internal Barbican.Core.Plugins.Pages.PluginPageRegistry? Pages { get; } = pages;

    internal IPluginPageOpener? PageOpener { get; } = pageOpener;

    /// <summary>Takes the one save-dialog slot; false while another add-on call has the dialog open, so add-ons cannot stack dialogs.</summary>
    internal bool TryBeginSave() => Interlocked.CompareExchange(ref _saving, 1, 0) == 0;

    internal void EndSave() => Volatile.Write(ref _saving, 0);
}
