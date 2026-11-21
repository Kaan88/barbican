#if WINDOWS
using Barbican.App.Composition;
using Barbican.App.Shell;
using Barbican.Core.Storage;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Creates each plugin WebView with its own browser data folder. Background runtimes draw nothing and take no input; an interactive view is a visible slot frame.</summary>
internal sealed class PluginSandboxWindowsViewFactory(
    DispatcherQueue queue, Func<Panel> getMount, IProfilePaths profile, HiddenWindowOptions options, bool interactive = false) : IPluginSandboxWebViewFactory
{
    private readonly DispatcherQueueDispatcher _ui = new(queue);

    public async Task<IPluginSandboxWebView> CreateAsync(string instanceProfile, CancellationToken ct)
    {
        if (!instanceProfile.StartsWith("plugin-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(instanceProfile.AsSpan(7), "N", out _))
            throw new ArgumentException("A native plugin instance profile is required.", nameof(instanceProfile));

        // Read before hopping to the UI queue: a slot start's mount flows with this call chain, not with the queue's callback.
        var chosen = getMount();
        var folder = Path.Combine(profile.DataDir, "PluginWebView2", instanceProfile);
        PluginSandboxWindowsView? created = null;
        var temporaryProfile = new PluginSandboxTemporaryProfile(folder);
        try
        {
            await _ui.InvokeAsync(async () => created = await CreateOnUiAsync(chosen, temporaryProfile, ct)).ConfigureAwait(false);
            return created!;
        }
        finally
        {
            if (created is null) await temporaryProfile.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<PluginSandboxWindowsView> CreateOnUiAsync(Panel mount, PluginSandboxTemporaryProfile temporaryProfile, CancellationToken ct)
    {
        RequireMount(mount);
        temporaryProfile.Reserve();
        var environment = await WebViewEnvironment.CreateWindowsAsync(temporaryProfile.Folder, options.IsHidden, ct);
        RequireMount(mount);

        var view = new WebView2 { IsHitTestVisible = interactive, IsTabStop = interactive };
        try
        {
            temporaryProfile.MarkBrowserStarting(environment);
            mount.Children.Add(view);
            await view.EnsureCoreWebView2Async(environment);
            ct.ThrowIfCancellationRequested();
            RequireMount(mount);
            var core = view.CoreWebView2;
            WebViewEnvironment.ValidateFolder(temporaryProfile.Folder, core.Environment.UserDataFolder);
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            return new PluginSandboxWindowsView(view, mount, temporaryProfile);
        }
        catch
        {
            try
            {
                view.Close();
                temporaryProfile.MarkBrowserClosed();
            }
            finally { mount.Children.Remove(view); }
            throw;
        }
    }

    private static void RequireMount(Panel mount)
    {
        if (!mount.IsLoaded || mount.XamlRoot is null)
            throw new InvalidOperationException("The plugin WebView mount is unavailable.");
    }
}
#endif
