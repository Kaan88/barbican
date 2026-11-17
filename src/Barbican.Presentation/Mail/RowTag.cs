// Mirrors: Bulwark lib/thread-utils.ts:258-279 getEmailTagIds ($label: and the legacy $color: prefix, only keywords set to true),
// hooks/use-keyword-format.ts:37 tagName, :57 tagColor, :76 sortTagIds (the order of the tag list, then by name), components/email/tag-badge.tsx
// (a lozenge with the name; the dot carries the first letter, WCAG 1.4.1). Owner mailcore.
// M2 hotfix 2 (the user: "tagging works but nothing shows it"): rows and the reader header draw the tags a message carries.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>One tag a message carries, as a row or the reader header draws it.</summary>
/// <param name="Id">The tag id (the keyword is <c>$label:id</c>).</param>
/// <param name="Name">The tag's name from the tag list; the id when the list does not know the tag.</param>
/// <param name="Color">0xRRGGBB, already in the colour-blind-safe palette when that setting is on.</param>
public sealed record RowTag(string Id, string Name, uint Color)
{
    private const string Prefix = "$label:";
    private const string LegacyPrefix = "$color:";

    /// <summary>The first letter of the name, upper case: what a dot shows, so no tag is told apart by colour alone.</summary>
    public string Initial => Name.Trim() is { Length: > 0 } n ? char.ToUpperInvariant(n[0]).ToString() : string.Empty;

    /// <summary>The tag ids of a message, each once (Bulwark getEmailTagIds).</summary>
    public static IReadOnlyList<string> IdsOf(Keywords keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        var ids = new List<string>();
        foreach (var (key, on) in keywords.Map)
        {
            if (!on)
            {
                continue;
            }

            var id = key.StartsWith(Prefix, StringComparison.Ordinal) ? key[Prefix.Length..]
                : key.StartsWith(LegacyPrefix, StringComparison.Ordinal) ? key[LegacyPrefix.Length..]
                : null;
            if (id is { Length: > 0 } && !ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>The tags of a message in the tag list's order, unknown tags last, ties by name (Bulwark sortTagIds).</summary>
    public static IReadOnlyList<RowTag> Of(Keywords keywords, TagLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return [.. IdsOf(keywords)
            .Select(id => (Tag: new RowTag(id, lookup.Name(id), lookup.Color(id)), Rank: lookup.Rank(id)))
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.Tag.Name, StringComparer.CurrentCulture)
            .Select(t => t.Tag)];
    }
}
