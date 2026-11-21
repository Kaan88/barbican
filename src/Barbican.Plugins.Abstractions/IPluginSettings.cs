// Mirrors: nothing in Bulwark (native add-on host services, API 1 additive; decision D-M6b-08). Per-account values of the add-on, kept in the profile's desktop settings file.

namespace Barbican.Plugins;

/// <summary>The add-on's own per-account settings. Values are text ("true" / "false" for a toggle); a key with no row can hold any text the add-on needs (it is kept but not drawn).</summary>
public interface IPluginSettings
{
    /// <summary>Loads the stored values off the UI thread; call before <see cref="Read"/>.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>The stored text, or null when nothing is stored. A memory read.</summary>
    string? Read(string accountKey, string key);

    /// <summary>Stores several values of one account at once (null removes a key); the file is written off the UI thread.</summary>
    Task SetAsync(string accountKey, IReadOnlyDictionary<string, string?> values, CancellationToken ct);

    /// <summary>Raised after a value changed (here or in another window), on a pool thread.</summary>
    event EventHandler? Changed;
}
