// Mirrors: Rust settings.rs:185 put (store one value or remove it, then save in the background); Bulwark stores/settings-store.ts setters.

using System.Text.Json.Nodes;

namespace Barbican.Core.Storage;

/// <summary>Changes the user's preferences (the settings pages are M6; M1 changes a few keys, such as the theme on the sign-in screen).</summary>
public interface IPrefsEditor
{
    /// <summary>Stores one value (null removes the key); the snapshot changes at once and the file is written shortly after, off the UI thread.</summary>
    void Put(string key, JsonNode? value);

    /// <summary>Writes a pending save now (call before the process exits; Rust settings.rs:122 flush).</summary>
    Task FlushAsync(CancellationToken ct);
}
