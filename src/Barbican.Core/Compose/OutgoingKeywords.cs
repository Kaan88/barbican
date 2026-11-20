// Mirrors: Bulwark lib/outgoing/outgoing-keywords.ts; RFC 8621 section 4.1.1.
namespace Barbican.Core.Compose;

/// <summary>Allowed add-on and selected-tag keywords for a sent message.</summary>
public static class OutgoingKeywords
{
    public static IReadOnlyList<string> Clean(IEnumerable<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in keywords)
        {
            if (keyword is null || keyword.Length is 0 or > 255 ||
                keyword.Any(c => c is < '!' or > '~' or '(' or ')' or '{' or ']' or '%' or '*' or '"' or '\\') ||
                (keyword.StartsWith('$') && !string.Equals(keyword, "$flagged", StringComparison.OrdinalIgnoreCase) &&
                 !(keyword.StartsWith("$label:", StringComparison.OrdinalIgnoreCase) && keyword.Length > 7)) || !seen.Add(keyword)) continue;
            result.Add(keyword);
            if (result.Count == 10) break;
        }
        return result;
    }
}
