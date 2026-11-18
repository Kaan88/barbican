// Mirrors: Bulwark stores/settings-store.ts (get, set, reset per key), Rust settings.rs :191 bool, :195 str; hooks onBeforeSettingChange (intercept: a plugin may veto or change the value) and onSettingChange (observer). B-SET-001..005, B-SET-008.
// THREADING (D-M6-00, binding on the real class and on every fake): Get is a memory read; SetAsync and ResetAsync complete off the UI thread (the prefs file is written on the pool); Changed is raised on the thread that finished the write, which is a pool thread: a view marshals it
// to its dispatcher before it touches a control (M5 found two such bugs hidden by sample stores that raised on the UI thread). Contract from the M6 plan section 4.2; owner set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Pages;

/// <summary>The settings values of the running profile (the Bulwark-keyed prefs).</summary>
public interface ISettingValues
{
    /// <summary>The stored value of a key (Bulwark's get), or null when unset (the caller falls back to the row's default).</summary>
    JsonNode? Stored(string key);

    /// <summary>Checks the value with <see cref="SettingRules.Coerce"/>, runs onBeforeSettingChange (a veto fails the call), writes prefs.json, raises <see cref="Changed"/>, then fires onSettingChange. Null resets the key.</summary>
    Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct);

    /// <summary>Raised after a key was written or reset, on a pool thread.</summary>
    event EventHandler<SettingChangedEventArgs>? Changed;
}
