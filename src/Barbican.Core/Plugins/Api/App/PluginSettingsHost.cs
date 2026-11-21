// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.settings.ts:1-14 (a change is written through the same setters the settings pages use, so plugin hooks run, the open page shows it and it syncs; then it is read back, since another add-on
// may have stopped it) and c.undo.ts (undo tokens). Barbican's setter is ISettingValues.SetAsync: Coerce, onBeforeSettingChange (a veto fails the write), prefs.json, Changed (the open Settings page updates), onSettingChange.
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Plugins.Api.App;

/// <summary>The settings store, the undo tokens and the budgets the app.*Settings methods share.</summary>
public sealed class PluginSettingsHost
{
    /// <summary>Reads of settings (getSettings, describeSettings) one add-on may make a minute. Bulwark has no limit; Barbican adds one.</summary>
    internal const int ReadsPerMinute = 60;

    /// <summary>Changes (setSettings, undoSettings) one add-on may make a minute. Bulwark has no limit; Barbican adds one.</summary>
    internal const int WritesPerMinute = 30;

    public PluginSettingsHost(ISettingValues values, IPluginMailRateLimiter limiter, TimeProvider time)
    {
        Values = values ?? throw new ArgumentNullException(nameof(values));
        Limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        Undo = new PluginUndoTokens(time ?? throw new ArgumentNullException(nameof(time)));
    }

    internal ISettingValues Values { get; }

    internal IPluginMailRateLimiter Limiter { get; }

    internal PluginUndoTokens Undo { get; }

    /// <summary>The kind of undo token app.setSettings issues (c.settings.ts:271).</summary>
    internal const string UndoKind = "settings";
}
