// Mirrors: Rust settings.rs:160 export, :167 import, :178 reset (the synchronous file side is Core/Settings/Prefs/SettingsStore.cs, T-259); hooks onSettingsExport, onSettingsImport (intercept), onSettingsReset (intercept). B-SET-006, I-450.
// THREADING as ISettingValues: the calls complete off the UI thread; Changed (every key may have changed: views reload) is raised on a pool thread. Contract from the M6 plan section 4.3; owner set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Backup;

/// <summary>Backup, restore and reset of the settings.</summary>
public interface ISettingBackup
{
    /// <summary>The export document (<c>{app, version, settings, desktop}</c>); the file is picked and written by the UI.</summary>
    Task<Result<JsonObject>> ExportAsync(CancellationToken ct);

    /// <summary>Replaces the settings with the document (a Bulwark export is accepted); the old prefs are kept as <c>prefs.json.bak</c> once.</summary>
    Task<Result<Unit>> ImportAsync(JsonNode document, CancellationToken ct);

    /// <summary>Clears every setting (the desktop file too); asks nothing: the UI asks first (D-7).</summary>
    Task<Result<Unit>> ResetAsync(CancellationToken ct);

    /// <summary>Raised after an import or a reset, on a pool thread.</summary>
    event EventHandler? Changed;
}
