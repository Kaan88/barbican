// Mirrors: see ISettingValues; Bulwark stores/settings-store.ts (updateSetting), Rust settings.rs:185 put. D-M6-A02: the only writer of prefs.json while the app runs; D-M6-A04 (onSettingsSync lives in SettingBackup); D-M6-A06 (onKeywordChange).
// Order of a write: Coerce, onBeforeSettingChange (a veto fails the call), PrefsStore.Put (the snapshot changes at once, the file follows off the UI thread), Changed, then onSettingChange.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;
using Barbican.Core.Settings.Prefs;

namespace Barbican.Core.Settings.Pages;

/// <summary>The default <see cref="ISettingValues"/>.</summary>
public sealed class SettingValues(IPrefsStore prefs, IPrefsEditor editor, IHookBus hooks, SettingWriteGate? gate = null) : ISettingValues, IDisposable
{
    // the keys whose change is a change of the reminder keywords (D-M6-A06)
    private static readonly string[] KeywordKeys = ["attachmentReminderKeywords"];

    private readonly SettingWriteGate _gate = gate ?? new SettingWriteGate();

    /// <inheritdoc/>
    public event EventHandler<SettingChangedEventArgs>? Changed;

    public void Dispose()
    {
        if (gate is null) _gate.Dispose();
    }

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

        // Interceptors may re-enter settings, so they run outside the gate. Retry if the
        // value changed while the interceptor was deciding; never apply stale approval.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var previous = Stored(key);
            if (!await hooks.AllowAsync(HookNames.OnBeforeSettingChange, () => new SettingChangePayload(key, next?.DeepClone(), previous?.DeepClone()), ct).ConfigureAwait(false))
            {
                return Result<Unit>.Fail(new ValidationError("A plug-in stopped this change."));
            }

            var applied = false;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (JsonNode.DeepEquals(previous, Stored(key)))
                {
                    editor.Put(key, next);
                    applied = true;
                }
            }
            finally
            {
                _gate.Release();
            }

            if (!applied)
            {
                continue;
            }

            Changed?.Invoke(this, new SettingChangedEventArgs(key, next?.DeepClone()));
            hooks.Fire(HookNames.OnSettingChange, () => new SettingChangePayload(key, next?.DeepClone(), previous?.DeepClone()));
            if (KeywordKeys.Contains(key, StringComparer.Ordinal))
            {
                var words = Words(next);
                hooks.Fire(HookNames.OnKeywordChange, () => new KeywordChangePayload(words));
            }

            return Result<Unit>.Ok(default);
        }

        return Result<Unit>.Fail(new ValidationError("The setting changed while it was being saved. Try again."));
    }

    private static string[] Words(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var text)
            ? text.Split([',', (char)10], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
