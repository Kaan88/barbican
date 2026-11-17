namespace Barbican.Core.Tests.Plugins;

/// <summary>Finds the repository's spec and source files from the test run directory.</summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    /// <summary>Reads a CSV file into rows of column name to value (RFC 4180 quoting, as the spec CSVs use it).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCsv(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Root, relativePath));
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        var header = rows[0];
        return [.. rows.Skip(1).Where(r => r.Count > 1).Select(r => (IReadOnlyDictionary<string, string>)header.Select((h, i) => (h, v: i < r.Count ? r[i] : string.Empty)).ToDictionary(x => x.h, x => x.v, StringComparer.Ordinal))];
    }

    /// <summary>The C# production sources under src/ (generated files and build output excluded).</summary>
    public static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "spec", "plugins", "hooks.csv")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("spec/plugins/hooks.csv not found above " + AppContext.BaseDirectory);
    }
}
