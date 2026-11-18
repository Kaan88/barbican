// Mirrors: see ISettingValues; Bulwark stores/settings-store.ts (updateSetting), Rust settings.rs:185 put. D-M6-A02: the only writer of prefs.json while the app runs; D-M6-A04 (onSettingsSync lives in SettingBackup); D-M6-A06 (onKeywordChange).
// Order of a write: Coerce, onBeforeSettingChange (a veto fails the call), PrefsStore.Put (the snapshot changes at once, the file follows off the UI thread), Changed, then onSettingChange.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;

namespace Barbican.Core.Settings.Pages;

/// <summary>The default <see cref="ISettingValues"/>.</summary>
public sealed class SettingValues(IPrefsStore prefs, IPrefsEditor editor, IHookBus hooks) : ISettingValues, IDisposable
{
    // the keys whose change is a change of the reminder keywords (D-M6-A06)
    private static readonly string[] KeywordKeys = ["attachmentReminderKeywords"];

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc/>
    public event EventHandler<SettingChangedEventArgs>? Changed;

    public void Dispose() => _gate.Dispose();

    /// <inheritdoc/>
    public JsonNode? Stored(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return prefs.Current.Values[key]?.DeepClone();
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Task.Run(() => WriteAsync(key, value, ct), ct);
    }

    private async Task<Result<Unit>> WriteAsync(string key, JsonNode? value, CancellationToken ct)
    {
        var row = SettingsCatalog.RowOfKey(key);
        var next = value?.DeepClone();
        if (row is not null)
        {
            var check = SettingRules.Coerce(row, value);
            if (!check.Ok)
            {
                return Result<Unit>.Fail(new ValidationError(check.Error ?? "That value is not allowed."));
            }

            next = check.Value;
        }

        // one writer at a time: the previous value a plug-in sees is the value this write replaces
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = Stored(key);
            if (!await hooks.AllowAsync(HookNames.OnBeforeSettingChange, () => new SettingChangePayload(key, next?.DeepClone(), previous?.DeepClone()), ct).ConfigureAwait(false))
            {
                return Result<Unit>.Fail(new ValidationError("A plug-in stopped this change."));
            }

            editor.Put(key, next);
            Changed?.Invoke(this, new SettingChangedEventArgs(key, next?.DeepClone()));
            hooks.Fire(HookNames.OnSettingChange, () => new SettingChangePayload(key, next?.DeepClone(), previous?.DeepClone()));
            if (KeywordKeys.Contains(key, StringComparer.Ordinal))
            {
                var words = Words(next);
                hooks.Fire(HookNames.OnKeywordChange, () => new KeywordChangePayload(words));
            }

            return Result<Unit>.Ok(default);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private static string[] Words(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text)
            ? text.Split([',', (char)10], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
