// Mirrors: Rust settings.rs Settings::{str,bool,with_prefs} (read side); Bulwark stores/settings-store.ts keys. Stub from F2; owner accounts.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

// CA1822: instance members of a stub that no stream has implemented yet; each gains state when its stream lands.
#pragma warning disable CA1822

namespace Barbican.Core.Storage;

/// <summary>A read-only view of prefs.json with typed reads and defaults.</summary>
public sealed class PrefsSnapshot
{
    private readonly JsonObject _values;

    private PrefsSnapshot(JsonObject values) => _values = values;

    /// <summary>The raw values (do not mutate).</summary>
    public JsonObject Values => _values;

    /// <summary>A snapshot over the given values (Rust <c>Settings::with_prefs</c>).</summary>
    public static PrefsSnapshot FromJson(JsonObject values) => throw new PortPendingException("PrefsSnapshot.FromJson: accounts stream");

    /// <summary>A string pref, or the default when absent or not a string.</summary>
    public string Str(string key, string fallback) => throw new PortPendingException("PrefsSnapshot.Str: accounts stream");

    /// <summary>A boolean pref (also accepts "true"/"false" strings), or the default.</summary>
    public bool Bool(string key, bool fallback) => throw new PortPendingException("PrefsSnapshot.Bool: accounts stream");

    /// <summary>A numeric pref (a number or a numeric string), or the default.</summary>
    public double Number(string key, double fallback) => throw new PortPendingException("PrefsSnapshot.Number: accounts stream");
}
