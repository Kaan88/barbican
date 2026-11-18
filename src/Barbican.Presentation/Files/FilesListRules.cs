// Mirrors: Bulwark components/files/file-views.tsx, file-list-layout.ts (sort, folders first); Rust files.rs render (name order); G-015, B-FILE-014. Contract from the M5 plan section 4.11; owner files-ui.

using Barbican.Core.Files;

namespace Barbican.Presentation.Files;

/// <summary>The pure rules of the file list.</summary>
public static class FilesListRules
{
    /// <summary>Folders first (whatever the key and direction), then by the key: Name natural and case-insensitive ("file2" before "file10"), Modified, Size, Type (media type, then name); a tie falls back to Name; the order is stable.</summary>
    public static IReadOnlyList<FileEntry> Sort(IReadOnlyList<FileEntry> entries, FilesSortKey key, bool descending) 
    {
        ArgumentNullException.ThrowIfNull(entries);
        Comparison<FileEntry> byKey = key switch
        {
            FilesSortKey.Modified => (a, b) => Nullable.Compare(a.Modified, b.Modified),
            FilesSortKey.Size => (a, b) => a.Size.CompareTo(b.Size),
            FilesSortKey.Type => (a, b) => string.Compare(a.MediaType, b.MediaType, StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => 0,
        };
        var sign = descending ? -1 : 1;
        // OrderBy is stable; folders first whatever the direction, a tie falls back to the name (ascending).
        return [.. entries
            .Select((e, i) => (e, i))
            .OrderBy(t => t, Comparer<(FileEntry e, int i)>.Create((x, y) =>
            {
                if (x.e.IsDirectory != y.e.IsDirectory)
                {
                    return x.e.IsDirectory ? -1 : 1;
                }

                var c = key == FilesSortKey.Name ? NaturalCompare(x.e.Name, y.e.Name) * sign : byKey(x.e, y.e) * sign;
                if (c == 0)
                {
                    c = NaturalCompare(x.e.Name, y.e.Name);
                }

                return c != 0 ? c : x.i.CompareTo(y.i);
            }))
            .Select(t => t.e)];
    }

    /// <summary>Case-insensitive compare where a run of digits counts by its value ("file2" before "file10").</summary>
    public static int NaturalCompare(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var si = i;
                var sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var da = a[si..i].TrimStart('0');
                var db = b[sj..j].TrimStart('0');
                var c = da.Length != db.Length ? da.Length.CompareTo(db.Length) : string.CompareOrdinal(da, db);
                if (c != 0)
                {
                    return c;
                }
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0)
                {
                    return c;
                }

                i++;
                j++;
            }
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Hidden files (a name starting with a dot) are dropped unless <paramref name="showHidden"/>; the trash folder is always dropped (it is a place of its own).</summary>
    public static IReadOnlyList<FileEntry> Visible(IReadOnlyList<FileEntry> entries, bool showHidden) 
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. entries.Where(e => !(e.IsDirectory && string.Equals(e.Name, FileTrash.TrashName, StringComparison.Ordinal))
            && (showHidden || !e.Name.StartsWith('.')))];
    }
}
