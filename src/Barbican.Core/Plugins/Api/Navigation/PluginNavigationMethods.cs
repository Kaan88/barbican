// Composition helper: the navigation, refresh and download adapters of one host, registered method by method in the app's container.
// ui.openPage (needs ui.setPages, which is not offered), search.run, search.clear and tabs.* are not part of this set (see docs/milestones/M6b-api-inventory.md).
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>Factory for the navigation methods.</summary>
public static class PluginNavigationMethods
{
    /// <summary>How many methods <see cref="Create"/> returns.</summary>
    public const int Count = 7;

    public static IReadOnlyList<IPluginApiMethod> Create(PluginNavigationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return
        [
            new PluginUiOpenEmail(host), new PluginUiOpenCalendar(host), new PluginUiOpenTask(host),
            new PluginUiRefresh(host, PluginUiRefresh.Kind.RowBadges), new PluginUiRefresh(host, PluginUiRefresh.Kind.RerenderEmail), new PluginUiRefresh(host, PluginUiRefresh.Kind.RerenderFetchedEmails),
            new PluginUiDownloadFile(host),
        ];
    }
}
