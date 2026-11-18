// Mirrors: see ISettingBackup; Rust settings.rs:160 export, :167 import, :178 reset (the pure file side is Core/Settings/Prefs/SettingsStore.cs, T-259); Bulwark components/settings/about-data-settings.tsx (export, import, reset).
// While the app runs prefs.json belongs to PrefsStore (one snapshot in memory), so import and reset go through IPrefsEditor key by key and flush; the desktop file (settings.json) is written atomically beside it. D-M6-A02, D-M6-A04.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Settings.Prefs;
using Barbican.Core.Storage;

namespace Barbican.Core.Settings.Backup;

/// <summary>The default <see cref="ISettingBackup"/>.</summary>
public sealed class SettingBackup(IPrefsStore prefs, IPrefsEditor editor, IProfilePaths paths, IHookBus hooks, SettingWriteGate? gate = null) : ISettingBackup, IDisposable
{
    private readonly SettingWriteGate _gate = gate ?? new SettingWriteGate();

    /// <inheritdoc/>
    public event EventHandler? Changed;

    public void Dispose()
    {
        if (gate is null) _gate.Dispose();
    }

    private string DesktopPath => Path.Combine(paths.ConfigDir, SettingsStore.DesktopFileName);

    /// <inheritdoc/>
    public Task<Result<JsonObject>> ExportAsync(CancellationToken ct) => Task.Run(() => ExportCoreAsync(ct), ct);

    private async Task<Result<JsonObject>> ExportCoreAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        JsonObject export;
        List<string> keys;
        try
        {
            var settings = (JsonObject)prefs.Current.Values.DeepClone();
            export = new JsonObject { ["app"] = "barbican", ["version"] = 1, ["settings"] = settings, ["desktop"] = ReadDesktop() ?? [] };
            keys = settings.Select(kv => kv.Key).ToList();
        }
        finally
        {
            _gate.Release();
        }

        hooks.Fire(HookNames.OnSettingsExport, () => new SettingsKeysPayload(keys));
        return Result<JsonObject>.Ok(export);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> ImportAsync(JsonNode document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Task.Run(() => ImportCoreAsync(document, ct), ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> ResetAsync(CancellationToken ct) => Task.Run(() => ResetCoreAsync(ct), ct);

    private async Task<Result<Unit>> ResetCoreAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var key in prefs.Current.Values.Select(kv => kv.Key).ToList())
            {
                editor.Put(key, null);
            }

            await editor.FlushAsync(ct).ConfigureAwait(false);
            AtomicFile.Delete(DesktopPath);
            _gate.PublishDesktop([]);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        hooks.Fire(HookNames.OnSettingsReset, () => new EmptyPayload());
        return Result<Unit>.Ok(default);
    }

    private async Task<Result<Unit>> ImportCoreAsync(JsonNode document, CancellationToken ct)
    {
        var plan = SettingBackupRules.Plan(document, SettingsCatalog.Pages());
        if (plan is null)
        {
            return Result<Unit>.Fail(new ValidationError("This file has no settings."));
        }

        var root = (JsonObject)document;
        var settings = plan.IsBulwark ? (root["settings"] as JsonObject ?? root) : root["settings"] as JsonObject ?? [];
        var keys = settings.Select(kv => kv.Key).ToList();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // keep what was there beside the file: a wrong file is easy to import (Rust kept no copy)
            var old = prefs.Current.Values.ToJsonString();
            await AtomicFile.WriteAllTextAsync(Path.Combine(paths.ConfigDir, PrefsStore.FileName + ".bak"), old, ct).ConfigureAwait(false);
            foreach (var key in prefs.Current.Values.Select(kv => kv.Key).Where(k => !settings.ContainsKey(k)).ToList())
            {
                editor.Put(key, null);
            }

            foreach (var (key, value) in settings)
            {
                editor.Put(key, value?.DeepClone());
            }

            await editor.FlushAsync(ct).ConfigureAwait(false);
            if (plan.HasDesktop && root["desktop"] is JsonObject desktop)
            {
                await AtomicFile.WriteAllTextAsync(DesktopPath, desktop.ToJsonString(), ct).ConfigureAwait(false);
                _gate.PublishDesktop(desktop);
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        hooks.Fire(HookNames.OnSettingsImport, () => new SettingsKeysPayload(keys));
        hooks.Fire(HookNames.OnSettingsSync, () => new SettingsSyncPayload("pulled", "import"));
        return Result<Unit>.Ok(default);
    }

    private JsonObject? ReadDesktop()
    {
        var text = AtomicFile.ReadAllTextOrNull(DesktopPath);
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
