// Mirrors: D-M6-A02 (one writer of prefs.json: PrefsStore through IPrefsEditor), Bulwark stores/settings-store.ts. The stand-in ISettingValues of a sample-data run: it writes the real prefs of the run's own profile, so theme, style and the other live settings apply
// through the same IPrefsStore.Changed the shell already listens to (D-M6-B04). Completes and raises off the caller like the real service (D-M6-00). Owner set-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Storage;
using Barbican.Presentation.Settings.Sample;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>An <see cref="ISettingValues"/> over the profile's prefs.</summary>
public sealed class SetUiPrefsValues(IPrefsStore store, IPrefsEditor editor) : ISettingValues
{
    public event EventHandler<SettingChangedEventArgs>? Changed;

    public JsonNode? Stored(string key) => store.Current.Values[key]?.DeepClone();

    public async Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Put raises IPrefsStore.Changed on the calling thread and the shell (ShellViewModel.Refresh) redraws from it: it must run on the caller's (UI) thread, as every M1 caller does.
        // The call still completes on the pool and raises Changed there, like the real service (D-M6-00).
        editor.Put(key, value?.DeepClone());
        await SampleThreading.OffThreadAsync(() => { }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new SettingChangedEventArgs(key, value?.DeepClone())));
        return Result<Unit>.Ok(default);
    }
}
