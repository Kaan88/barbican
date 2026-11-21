// Mirrors: Bulwark lib/plugin-sandbox/page-navigation.ts:14-62 (the router handler PluginPageNavigator sets moves the tab to /apps/<pluginId>/<pageId>/<path>; false while nothing is mounted to take it).
// The shell shows the page through the same state a click on its rail entry sets (ShellViewModel.OpenPluginPage); it runs on the UI thread and rechecks that the page is still the add-on's.
using Barbican.Core.Plugins.Api.Navigation;
using Barbican.Core.Plugins.Pages;
using Barbican.Presentation.Common;
using Barbican.Presentation.Shell;

namespace Barbican.App.Plugins;

internal sealed class PluginPageOpener(ShellViewModel shell, PluginPageRegistry pages, IDispatcher dispatcher) : IPluginPageOpener
{
    public async Task<bool> OpenPageAsync(string packageId, string pageId, string path, CancellationToken ct)
    {
        var moved = false;
        await dispatcher.InvokeAsync(() =>
        {
            if (pages.Find(packageId, pageId) is not { } page) return Task.CompletedTask;
            shell.OpenPluginPage(page.Key, path);
            moved = true;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return moved;
    }
}
