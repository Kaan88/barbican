// Mirrors: Bulwark stores/file-store.ts parentPath / folderPath and lib/plugin-projection.ts fileToView (the path of a hook payload: "/" for the root, "/Docs/Invoices"); Rust files.rs breadcrumb (:226, 64 levels at most). Owner files-data.

using System.Text;

namespace Barbican.Core.Files;

/// <summary>Display paths of nodes, from a node list.</summary>
public static class FilePaths
{
    private const int MaxDepth = 64;

    /// <summary>The path of the folder <paramref name="folderKey"/> (null: the root, "/"); "" when it is not in the list.</summary>
    public static string Of(IReadOnlyList<FileEntry> entries, string? folderKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (folderKey is null)
        {
            return "/";
        }

        var byKey = entries.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var names = new List<string>();
        var cur = folderKey;
        for (var i = 0; cur is not null && i < MaxDepth; i++)
        {
            if (!byKey.TryGetValue(cur, out var n))
            {
                return string.Empty;
            }

            names.Add(n.Name);
            cur = n.ParentKey;
        }

        if (cur is not null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = names.Count - 1; i >= 0; i--)
        {
            _ = sb.Append('/').Append(names[i]);
        }

        return sb.ToString();
    }
}
