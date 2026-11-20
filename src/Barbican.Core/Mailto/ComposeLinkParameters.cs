// Mirrors: Bulwark lib/plugin-link-params.ts (linkParamsFrom, splitLinkParams).
using System.Buffers;

namespace Barbican.Core.Mailto;

/// <summary>Untrusted link parameters, bounded and partitioned to their owning add-on.</summary>
public static class ComposeLinkParameters
{
    public const int MaxParameters = 50;
    public const int MaxNameLength = 128;
    public const int MaxValueLength = 2048;
    private static readonly SearchValues<char> NameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789._-");

    /// <summary>Filters already-decoded query pairs. The first occurrence of a name wins.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawName, rawValue) in parameters)
        {
            var name = rawName.Trim().ToLowerInvariant();
            if (!ValidName(name) || result.ContainsKey(name)) continue;
            if (result.Count >= MaxParameters) break;
            var clean = new string([.. rawValue.Where(c => c > '\u001f' && c != '\u007f').Take(MaxValueLength)]);
            result.Add(name, clean.Length > 0 && char.IsHighSurrogate(clean[^1]) ? clean[..^1] : clean);
        }
        return result;
    }

    /// <summary>Assigns each name to the longest matching owner ID, removing its prefix.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Partition(
        IReadOnlyDictionary<string, string> parameters, IEnumerable<string> pluginIds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(pluginIds);
        var ids = pluginIds.Distinct(StringComparer.Ordinal).OrderByDescending(id => id.Length).ToArray();
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            foreach (var id in ids)
            {
                var prefix = "x-" + id + "-";
                if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!result.TryGetValue(id, out var own))
                {
                    own = new Dictionary<string, string>(StringComparer.Ordinal);
                    result.Add(id, own);
                }
                ((Dictionary<string, string>)own).Add(name[prefix.Length..], value);
                break;
            }
        }
        return result;
    }

    private static bool ValidName(string name) => name.Length is > 2 and <= MaxNameLength &&
        name.StartsWith("x-", StringComparison.Ordinal) && char.IsAsciiLetterOrDigit(name[2]) &&
        name.AsSpan(2).IndexOfAnyExcept(NameCharacters) < 0;
}
