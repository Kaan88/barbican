using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Settings.Prefs;

/// <summary>A profile under a throwaway directory (the Rust test overrode APPDATA).</summary>
internal sealed class TestProfilePaths(string root) : IProfilePaths
{
    public string Profile => "test";

    public string ConfigDir => Path.Combine(root, "config");

    public string DataDir => Path.Combine(root, "data");
}
