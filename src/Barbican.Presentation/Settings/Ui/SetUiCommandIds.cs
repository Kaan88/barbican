// Mirrors: M6 plan section 3.3 (command ids of set-ui are settings.*); Bulwark components/settings/settings-app.tsx (open a tab, back, forward, Show advanced settings). Owner set-ui.

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The ids of the settings commands.</summary>
public static class SetUiCommandIds
{
    /// <summary>Opens the Settings app on the page named by the argument (a SettingsPageIds value), with an optional row after a colon.</summary>
    public const string OpenPage = "settings.open-page";

    public const string Back = "settings.back";

    public const string Forward = "settings.forward";

    public const string ToggleAdvanced = "settings.toggle-advanced";
}
