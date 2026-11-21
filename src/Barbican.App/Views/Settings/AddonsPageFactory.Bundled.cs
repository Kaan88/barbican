// Mirrors: Rust main.rs:455 (disabledPlugins in the prefs: a listed plugin is never initialized or called); decision D-M6b-08: the bundled native add-ons (spam score, the AI assistant) are listed here and can be turned off or on.
// A bundled add-on ships inside the app, so it can be disabled but not removed; the change applies at the next start (the native host reads the list when it loads).

using System.Text.Json.Nodes;
using Barbican.App.Composition.Features;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Barbican.App.Views.Settings;

internal sealed partial class AddonsPageFactory
{
    private StackPanel? _bundled;

    private async Task LoadBundledAsync()
    {
        if (_bundled is null) return;
        _ = await native.LoadAsync().ConfigureAwait(true);
        _bundled.Children.Clear();
        var bundled = native.Plugins.Where(p => p.Bundled).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        if (bundled.Count == 0) return;
        _bundled.Children.Add(SetUiUi.Heading("Built-in add-ons"));
        foreach (var plugin in bundled)
        {
            var id = plugin.Id;
            var detail = SetUiUi.Id(SetUiUi.Meta(string.Empty), "set.addons.bundled.state." + id);
            AutomationProperties.SetLiveSetting(detail, AutomationLiveSetting.Polite);
            var enabled = plugin.Enabled;
            detail.Text = $"Version {plugin.Version} · {(enabled ? "Enabled" : "Disabled")} · Native · {plugin.Description}";
            Button? toggle = null;
            toggle = SetUiUi.Pill(enabled ? "Disable" : "Enable", () =>
            {
                enabled = !enabled;
                SetBundledDisabled(id, !enabled);
                detail.Text = $"Version {plugin.Version} · {(enabled ? "Enabled" : "Disabled")} · Native · {plugin.Description} · Applies after you restart Barbican.";
                toggle!.Content = enabled ? "Disable" : "Enable";
                AutomationProperties.SetName(toggle, (enabled ? "Disable " : "Enable ") + id);
            }, "set.addons.bundled.toggle." + id);
            AutomationProperties.SetName(toggle, (enabled ? "Disable " : "Enable ") + id);
            _bundled.Children.Add(SetUiUi.RowWithDescription(plugin.Name, detail, toggle, "set.addons.bundled.item." + id));
        }
    }

    private void SetBundledDisabled(string id, bool disabled)
    {
        var current = prefs.Current.Values[UnifiedRegistration.DisabledPluginsKey] is JsonArray array
            ? array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList()
            : [];
        _ = current.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
        if (disabled) current.Add(id);
        editor.Put(UnifiedRegistration.DisabledPluginsKey, new JsonArray([.. current.Select(x => (JsonNode?)x)]));
    }
}
