// Helpers of the M5 file tests (the n() closure of Rust files.rs tests). Owner files-data; no other stream's test folder uses this name.

using Barbican.Core.Files;

namespace Barbican.Core.Tests.Files;

internal static class FileTestData
{
    /// <summary>A node of the primary account: key = id, parent key as given.</summary>
    public static FileEntry N(string id, string? parent, string name, bool dir) =>
        new(id, "a", false, id, parent, name, dir, dir ? null : "b", 0, null, null, null);
}
