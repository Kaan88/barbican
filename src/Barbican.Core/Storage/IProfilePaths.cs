// Mirrors: PORT-PLAN.md §3.5 (profiles: BARBICAN_PROFILE=dev, config and data dirs); Rust config_dir(). Contract stub from F2; owner accounts.

namespace Barbican.Core.Storage;

/// <summary>Where one profile keeps its files.</summary>
public interface IProfilePaths
{
    /// <summary>The profile name (<c>dev</c> in C# dev builds).</summary>
    string Profile { get; }

    /// <summary>Settings, prefs.json, accounts index.</summary>
    string ConfigDir { get; }

    /// <summary>Caches and other regenerable data.</summary>
    string DataDir { get; }
}
