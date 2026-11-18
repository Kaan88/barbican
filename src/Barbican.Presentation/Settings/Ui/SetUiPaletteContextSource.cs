// Mirrors: Rust palette.rs:70 Ctx (the settings tabs of the palette, "Go to X settings"); M6 plan section 3.1 (the palette entries are set-ui's). Wraps the M2 context source and adds the settings pages. Owner set-ui.

using Barbican.Presentation.Palette;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The palette context with the settings pages filled in.</summary>
public sealed class SetUiPaletteContextSource(IPaletteContextSource inner, ISettingsNavigation navigation, ISetUiViewModel screen) : IPaletteContextSource
{
    public PaletteContext Current() => inner.Current() with
    {
        SettingsTabs = [.. screen.VisiblePages.Select(p => (p.Id, p.Title))],
        SettingsTab = navigation.Current,
    };
}
