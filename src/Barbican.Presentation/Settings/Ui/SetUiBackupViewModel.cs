// Mirrors: Bulwark components/settings/about-data-settings.tsx (export, import, reset), Rust settings.rs:167 import; M6 plan section 4.3; I-449, I-450. The file is picked and written by the view; reset asks first through IDialogService (D-7, in-window).

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Backup;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>Export, import and reset of the settings.</summary>
public sealed class SetUiBackupViewModel(ISettingBackup backup, IDialogService? dialogs = null)
{
    /// <summary>The export document as text, or null (the reason is in the status).</summary>
    public async Task<(string? Text, string Status)> ExportAsync(CancellationToken ct)
    {
        try
        {
            var r = await backup.ExportAsync(ct).ConfigureAwait(true);
            return r.IsOk && r.Value is { } doc ? (doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), "Settings exported.") : (null, r.Error?.Message ?? "Export failed.");
        }
        catch (PortPendingException)
        {
            return (null, "Export is not available yet.");
        }
    }

    /// <summary>Imports the text of a backup file; the status says what happened.</summary>
    public async Task<string> ImportAsync(string text, CancellationToken ct)
    {
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return "That file is not a settings backup.";
        }

        if (doc is not JsonObject)
        {
            return "That file is not a settings backup.";
        }

        try
        {
            var r = await backup.ImportAsync(doc, ct).ConfigureAwait(true);
            return r.IsOk ? "Settings imported." : r.Error?.Message ?? "Import failed.";
        }
        catch (PortPendingException)
        {
            return "Import is not available yet.";
        }
    }

    /// <summary>Asks, then clears every setting.</summary>
    public async Task<string> ResetAsync(CancellationToken ct)
    {
        if (dialogs is not null)
        {
            var yes = await dialogs.ShowAsync(new ConfirmDialogRequest("Reset settings", "Every setting goes back to its default. Mail and accounts are not touched.", "Reset", true), ct).ConfigureAwait(true);
            if (!yes)
            {
                return string.Empty;
            }
        }

        try
        {
            var r = await backup.ResetAsync(ct).ConfigureAwait(true);
            return r.IsOk ? "Settings reset." : r.Error?.Message ?? "Reset failed.";
        }
        catch (PortPendingException)
        {
            return "Reset is not available yet.";
        }
    }
}
