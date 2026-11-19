// Source: Bulwark lib/extension-package.ts:54-96, 203-207; implicit directory alias and Windows path checks are host-added restrictions.
namespace Barbican.Core.Plugins.Packages.Inspection;

// Track implicit directories too: folder/A and FOLDER/b are an alias even when no directory entry exists.
internal sealed class PackagePaths
{
    private readonly Dictionary<string, (string Exact, bool Directory)> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool Add(string path, bool directory)
    {
        var trimmed = path.TrimEnd('/');
        if (!_entries.Add(trimmed)) return false;
        var parts = trimmed.Split('/');
        var current = string.Empty;
        for (var i = 0; i < parts.Length; i++)
        {
            current = current.Length == 0 ? parts[i] : current + "/" + parts[i];
            var isDirectory = i < parts.Length - 1 || directory;
            if (_seen.TryGetValue(current, out var prior))
            {
                if (!string.Equals(prior.Exact, current, StringComparison.Ordinal) || prior.Directory != isDirectory) return false;
            }
            else _seen.Add(current, (current, isDirectory));
        }
        return true;
    }
}
