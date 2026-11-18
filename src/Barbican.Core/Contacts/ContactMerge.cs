// Mirrors: Rust contact_merge.rs:19 name_key, :31 phone_key, :39 find_groups, :136 plan, :199 member_keys, :210 group_moves; Bulwark lib/contacts/merge.ts, components/contacts/contact-merge-dialog.tsx, contact-duplicates-view.tsx. B-CON-023, B-CON-024.
// Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>Finding and merging duplicate contacts.</summary>
public static partial class ContactMerge
{
    /// <summary>The comparison key of a card's name (lower-case, diacritics stripped, word order ignored: "Anna Müller" and "Muller, anna" match); empty without a name.</summary>
    public static string NameKey(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var name = ContactJson.Get(card, "name");
        var parts = ContactJson.Get(name, "components") is JsonArray a
            ? a.Where(x => x.Str("kind") is "given" or "surname" or "given2").Select(x => x.Str("value")).OfType<string>().ToList()
            : [];
        var text = parts.Count == 0 ? name.Str("full") ?? string.Empty : string.Join(' ', parts);
        var words = ContactText.Fold(text).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).ToList();
        words.Sort(StringComparer.Ordinal);
        var key = string.Join(' ', words);
        return key.Length >= 3 ? key : string.Empty;
    }

    /// <summary>The last eight digits of a phone number, so "+49 30 5550000" and "030 5550000" match; empty with fewer than seven digits ("12").</summary>
    public static string PhoneKey(string number)
    {
        ArgumentNullException.ThrowIfNull(number);
        var digits = new string([.. number.Where(char.IsAsciiDigit)]);
        return digits.Length >= 7 ? digits[Math.Max(0, digits.Length - 8)..] : string.Empty;
    }

    /// <summary>Groups of two or more card indexes that share a name key, an email or a phone key (transitively joined); groups (kind group) are ignored; each group is ascending and the groups are ordered by their first index.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> FindGroups(IReadOnlyList<JsonNode> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var idx = Enumerable.Range(0, cards.Count).Where(i => !string.Equals(cards[i].Str("kind"), "group", StringComparison.Ordinal)).ToList();
        var parent = Enumerable.Range(0, cards.Count).ToArray();
        int Find(int i)
        {
            var root = i;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[i] != root)
            {
                (parent[i], i) = (root, parent[i]);
            }

            return root;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in idx)
        {
            foreach (var k in Keys(cards[i]))
            {
                if (!seen.TryGetValue(k, out var first))
                {
                    seen[k] = i;
                }
                else
                {
                    parent[Find(i)] = Find(first);
                }
            }
        }

        var groups = new List<(int Root, List<int> Members)>();
        foreach (var i in idx)
        {
            var r = Find(i);
            var at = groups.FindIndex(g => g.Root == r);
            if (at < 0)
            {
                groups.Add((r, [i]));
            }
            else
            {
                groups[at].Members.Add(i);
            }
        }

        return [.. groups.Where(g => g.Members.Count > 1).Select(g => (IReadOnlyList<int>)g.Members)];
    }

    /// <summary>The keys a card is compared by: its name key, each email (trimmed, lower case) and each phone key.</summary>
    private static List<string> Keys(JsonNode c)
    {
        var keys = new List<string>();
        if (NameKey(c) is { Length: > 0 } n)
        {
            keys.Add("n|" + n);
        }

        foreach (var e in ContactJson.Values(c, "emails"))
        {
            if ((e.Str("address") ?? string.Empty).Trim().ToLowerInvariant() is { Length: > 0 } a)
            {
                keys.Add("e|" + a);
            }
        }

        foreach (var p in ContactJson.Values(c, "phones"))
        {
            if (PhoneKey(p.Str("number") ?? string.Empty) is { Length: > 0 } k)
            {
                keys.Add("p|" + k);
            }
        }

        return keys;
    }

    /// <summary>The keys under which a card is a group member: its uid, the uid with a "urn:uuid:" prefix removed (or added when it has none), and its id.</summary>
    public static IReadOnlyList<string> MemberKeys(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var keys = new List<string>();
        if (card.Str("uid") is { Length: > 0 } uid)
        {
            keys.Add(uid);
            keys.Add(uid.StartsWith("urn:uuid:", StringComparison.Ordinal) ? uid[9..] : "urn:uuid:" + uid);
        }

        if (card.Str("id") is { } id)
        {
            keys.Add(id);
        }

        return keys;
    }

    /// <summary>For each group that has a merged-away card as a member: (group id, the new members map) with those cards replaced by the kept card.</summary>
    public static IReadOnlyList<(string GroupId, JsonObject Members)> GroupMoves(IReadOnlyList<JsonNode> groups, JsonNode keep, IReadOnlyList<JsonNode> others)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(others);
        var otherKeys = others.SelectMany(MemberKeys).ToHashSet(StringComparer.Ordinal);
        var keepKeys = MemberKeys(keep);
        var moves = new List<(string, JsonObject)>();
        foreach (var g in groups)
        {
            if (ContactJson.Get(g, "members") is not JsonObject m)
            {
                continue;
            }

            var listed = m.Where(kv => ContactJson.IsTrue(kv.Value) && otherKeys.Contains(kv.Key)).Select(kv => kv.Key).ToList();
            if (listed.Count == 0)
            {
                continue;
            }

            var members = (JsonObject)m.DeepClone();
            foreach (var k in listed)
            {
                _ = members.Remove(k);
            }

            if (!keepKeys.Any(k => ContactJson.IsTrue(members[k])))
            {
                var key = keep.Str("uid") is { Length: > 0 } uid ? uid : keep.Str("id") ?? string.Empty;
                members[key] = true;
            }

            moves.Add((g.Str("id") ?? string.Empty, members));
        }

        return moves;
    }
}
