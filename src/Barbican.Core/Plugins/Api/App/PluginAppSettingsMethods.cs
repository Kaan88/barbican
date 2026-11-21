// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:204-206,2226-2227 and assistant/c.ts:33-36,95-97 (app.searchSettings no static gate, app.getSettings and app.describeSettings settings:read, app.setSettings and app.undoSettings settings:change).
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.App;

/// <summary>Factory for the app.*Settings methods (the rest of the app.* family, app.getInfo, is <see cref="PluginAppGetInfo"/>).</summary>
public static class PluginAppSettingsMethods
{
    /// <summary>How many methods <see cref="Create"/> returns.</summary>
    public const int Count = 5;

    public static IReadOnlyList<IPluginApiMethod> Create(PluginSettingsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return [new PluginAppSearchSettings(host), new PluginAppGetSettings(host), new PluginAppDescribeSettings(host), new PluginAppSetSettings(host), new PluginAppUndoSettings(host)];
    }
}
