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
            var enabled = plugin.Enabled;
            var detail = SetUiUi.Id(SetUiUi.Meta(string.Empty), "set.addons.bundled.state." + id);
            AutomationProperties.SetLiveSetting(detail, AutomationLiveSetting.Polite);
            void Describe(bool changed) =>
                detail.Text = $"v{plugin.Version} · {(enabled ? "Enabled" : "Disabled")} · Native" + (changed ? " · Applies after you restart Barbican." : string.Empty);
            Describe(changed: false);
            // plugins-settings.tsx:262-270: the switch; the change applies at the next start (the host reads the list when it loads)
            var toggle = SetUiUi.Id(new ToggleSwitch { IsOn = enabled, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 }, "set.addons.bundled.toggle." + id);
            AutomationProperties.SetName(toggle, "Turn " + plugin.Name + " on or off");
            toggle.Toggled += (_, _) =>
            {
                if (toggle.IsOn == enabled) return;
                enabled = toggle.IsOn;
                SetBundledDisabled(id, !enabled);
                Describe(changed: true);
            };
            _bundled.Children.Add(SetUiAddonCard.Build(plugin.Name, plugin.Description, detail, [("Built-in", SetUiAddonCard.BadgeKind.Quiet)], toggle, null, "set.addons.bundled.item." + id));
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
