// Mirrors: Rust settings.rs (prefs.json, Bulwark settings-store keys; JsonNode-preserving). Contract stub from F2; owner accounts.

namespace Barbican.Core.Storage;

/// <summary>The user's preferences (prefs.json with Bulwark's keys).</summary>
public interface IPrefsStore
{
    /// <summary>The current values.</summary>
    PrefsSnapshot Current { get; }

    /// <summary>Raised after the values changed.</summary>
    event EventHandler? Changed;
}
