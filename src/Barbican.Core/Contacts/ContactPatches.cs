// Mirrors: Rust contacts.rs:319 ptr, :351 set_member, :361 keywords_with (Stalwart crates/jmap/src/contact/set.rs refuses a keywords/x pointer patch on a card that has no keywords, so a category change sends the whole map, as Bulwark does); RFC 8620 5.3 (a patch is a map of JSON pointers). B-CON-006. Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

/// <summary>Patches the contact writer and the views build.</summary>
public static class ContactPatches
{
    /// <summary>A map key as one JSON pointer segment ("~" is "~0", "/" is "~1").</summary>
    public static string PointerSegment(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    /// <summary>The card's whole keywords map with the categories in <paramref name="set"/> on and those in <paramref name="clear"/> gone.</summary>
    public static JsonObject Keywords(JsonNode? card, IEnumerable<string> set, IEnumerable<string> clear)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(clear);
        var m = ContactJson.Get(card, "keywords") is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        foreach (var k in clear)
        {
            _ = m.Remove(k);
        }

        foreach (var k in set)
        {
            m[k] = true;
        }

        return m;
    }

    /// <summary>The member keys a group lists a card under ("urn:uuid:" and a bare uid are the same card).</summary>
    public static IReadOnlyList<string> KeysOf(JsonNode? group, string uid)
    {
        ArgumentNullException.ThrowIfNull(uid);
        var bare = Bare(uid);
        return ContactJson.Get(group, "members") is JsonObject m ? [.. m.Select(kv => kv.Key).Where(k => string.Equals(Bare(k), bare, StringComparison.Ordinal))] : [];
    }

    /// <summary>The patch of a group's members: members/&lt;uid&gt; true for each added, null for each key a removed card is listed under.</summary>
    public static JsonObject Members(JsonNode? group, IEnumerable<string> add, IEnumerable<string> remove)
    {
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        var patch = new JsonObject();
        foreach (var uid in remove)
        {
            foreach (var key in KeysOf(group, uid))
            {
                patch["members/" + PointerSegment(key)] = null;
            }
        }

        foreach (var uid in add)
        {
            patch["members/" + PointerSegment(uid)] = true;
        }

        return patch;
    }

    private static string Bare(string key) => key.StartsWith("urn:uuid:", StringComparison.Ordinal) ? key[9..] : key;
}
