// Mirrors: Rust files.rs:127 unique_name; Bulwark lib/files-upload-conflicts.ts (getUniqueName: "name (2).ext"). Contract from the M5 plan section 4.6; owner files-data.

namespace Barbican.Core.Files;

/// <summary>Name arithmetic of the Files app.</summary>
public static class FileNames
{
    /// <summary>The name itself when free, else "stem (2).ext", "stem (3).ext", ... (a leading dot is not an extension: ".bashrc (2)").</summary>
    public static string UniqueName(string name, ISet<string> used) => throw new Common.PortPendingException("FileNames.UniqueName");
}
