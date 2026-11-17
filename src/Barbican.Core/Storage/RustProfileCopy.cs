// Mirrors: PORT-PLAN.md §3.5 ("on first run, the dev profile imports a copy of the Rust app's prefs, panes and search history; it never writes
// the Rust profile"); Rust settings.rs:35 (prefs.json), main.rs panes.json, search_history.rs (search-history.json), all in config_dir()
// (platform/mod.rs:20). Read-only on the Rust side: files are opened for reading only and copied, never moved.

namespace Barbican.Core.Storage;

/// <summary>The dev profile's one-time copy of the Rust app's preferences.</summary>
public static class RustProfileCopy
{
    /// <summary>The files copied (all optional).</summary>
    public static readonly IReadOnlyList<string> Files = [PrefsStore.FileName, "panes.json", "search-history.json"];

    /// <summary>Written into the profile once the copy ran, so it never runs again (even when the user later deletes a copied file).</summary>
    public const string Marker = ".copied-from-rust";

    /// <summary>
    /// Copies the files from <paramref name="rustConfigDir"/> into the profile when the profile is <c>dev</c> and the copy never ran. A file the
    /// profile already has is kept. Returns the names copied.
    /// </summary>
    public static IReadOnlyList<string> Run(IProfilePaths profile, string rustConfigDir)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(rustConfigDir);
        IoThreadCheck.AssertNotUiThread();
        var marker = Path.Combine(profile.ConfigDir, Marker);
        if (!string.Equals(profile.Profile, ProfilePaths.DefaultProfile, StringComparison.Ordinal) || File.Exists(marker)
            || string.Equals(Path.GetFullPath(rustConfigDir), Path.GetFullPath(profile.ConfigDir), StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var copied = new List<string>();
        foreach (var name in Files)
        {
            var target = Path.Combine(profile.ConfigDir, name);
            if (File.Exists(target) || AtomicFile.ReadAllTextOrNull(Path.Combine(rustConfigDir, name)) is not { } text)
            {
                continue;
            }

            AtomicFile.WriteAllText(target, text);
            copied.Add(name);
        }

        AtomicFile.WriteAllText(marker, string.Join('\n', copied));
        return copied;
    }
}
