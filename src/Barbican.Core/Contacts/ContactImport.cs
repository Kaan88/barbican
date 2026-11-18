// Mirrors: Rust vcard.rs:667 detect_duplicates, contacts_x.rs:752 import_batch, :170 new_uid, :909 principal_card; Bulwark components/contacts/contact-import-dialog.tsx, lib/contacts/. B-CON-016, B-CON-022. Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>Pure rules of the vCard import.</summary>
public static class ContactImport
{
    /// <summary>Indexes of incoming cards with an email (trimmed, case-insensitive) that is already known: in <paramref name="existing"/> or in an earlier incoming card.</summary>
    public static IReadOnlyList<int> DetectDuplicates(IReadOnlyList<JsonNode> existing, IReadOnlyList<JsonNode> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        static List<string> Addresses(JsonNode c) => [.. ContactJson.Values(c, "emails").Select(e => (e.Str("address") ?? string.Empty).Trim().ToLowerInvariant()).Where(a => a.Length > 0)];
        var known = existing.SelectMany(Addresses).ToHashSet(StringComparer.Ordinal);
        var duplicates = new List<int>();
        for (var i = 0; i < incoming.Count; i++)
        {
            var a = Addresses(incoming[i]);
            if (a.Any(known.Contains))
            {
                duplicates.Add(i);
                continue;
            }

            known.UnionWith(a);
        }

        return duplicates;
    }

    /// <summary>The cards to create: duplicates left out when <paramref name="skipDuplicates"/>, every card in <paramref name="bookId"/>, the id removed, a uid ("urn:uuid:" and a GUID) when it has none.</summary>
    public static IReadOnlyList<JsonNode> Batch(IReadOnlyList<JsonNode> cards, IReadOnlyList<int> duplicates, bool skipDuplicates, string bookId)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(duplicates);
        ArgumentNullException.ThrowIfNull(bookId);
        var skip = skipDuplicates ? duplicates.ToHashSet() : [];
        var batch = new List<JsonNode>();
        for (var i = 0; i < cards.Count; i++)
        {
            if (skip.Contains(i) || cards[i] is not JsonObject source)
            {
                continue;
            }

            var card = (JsonObject)source.DeepClone();
            _ = card.Remove("id");
            card["addressBookIds"] = new JsonObject { [bookId] = true };
            if (!card.ContainsKey("uid"))
            {
                card["uid"] = NewUid();
            }

            batch.Add(card);
        }

        return batch;
    }

    /// <summary>"urn:uuid:" followed by a lower-case GUID (45 characters).</summary>
    public static string NewUid() => "urn:uuid:" + Guid.NewGuid().ToString("D");

    /// <summary>A directory principal (id, type, name, email, description) as a read-only card: kind group or individual, one email, the description as a note.</summary>
    public static JsonNode PrincipalCard(JsonNode principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var kind = principal.Str("type") switch { "group" => "group", "location" => "location", "resource" => "device", _ => "individual" };
        var c = new JsonObject { ["name"] = new JsonObject { ["full"] = principal.Str("name") ?? string.Empty }, ["kind"] = kind };
        if (principal.Str("email") is { Length: > 0 } email)
        {
            c["emails"] = new JsonObject { ["e0"] = new JsonObject { ["@type"] = "EmailAddress", ["address"] = email, ["contexts"] = new JsonObject { ["work"] = true } } };
        }

        if (principal.Str("description") is { Length: > 0 } description)
        {
            c["notes"] = new JsonObject { ["n0"] = new JsonObject { ["@type"] = "Note", ["note"] = description } };
        }

        if (principal.Str("timeZone") is { } tz)
        {
            c["addresses"] = new JsonObject { ["a0"] = new JsonObject { ["@type"] = "Address", ["timeZone"] = tz, ["full"] = $"Time zone: {tz}" } };
        }

        return c;
    }
}
