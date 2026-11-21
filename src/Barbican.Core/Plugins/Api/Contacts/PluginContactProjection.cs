// Mirrors: Bulwark lib/jmap/types.ts:339-387 (ContactCard), :550-572 (AddressBook, AddressBookRights) and lib/jmap/contact-wire.ts:107-119 contactFromWire (the flat calendarUri, freeBusyUri, schedulingUri and source views of RFC 9553 properties).
// RFC 9553 (JSContact) section 2; RFC 9610 (AddressBook). Every top-level field is named here: nothing is copied by reflection, so a field Barbican adds to a stored card (ids of other accounts, "_" bookkeeping) never
// reaches a plugin. Deviations: Bulwark hands a plugin the whole card including accountId and accountName and an address book's shareWith; here those stay out (a shared card says isShared and originalId only; the book
// has no shareWith, which names other people's principal ids). Nested values (a name, an email entry) are the server's own JSContact data, copied without keys that start with "_".
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Plugins.Api.Contacts;

internal static class PluginContactProjection
{
    /// <summary>A card of a few kilobytes; photos are shrunk to a few tens of kilobytes.</summary>
    internal static readonly PluginValueLimits Limits = new(MaxWireBytes: 48 * 1024 * 1024, MaxNodes: 400_000, MaxStringBytes: 2 * 1024 * 1024, MaxSequenceLength: 100_000);

    internal const int MaxDepth = 24;

    /// <summary>The JSContact properties of a card a plugin sees (types.ts:339-387), in the order the card type lists them.</summary>
    internal static readonly IReadOnlyList<string> CardKeys =
    [
        "@type", "version", "uid", "addressBookIds", "kind", "language", "name", "nicknames", "emails", "phones", "onlineServices", "preferredLanguages", "organizations", "titles", "addresses", "anniversaries",
        "personalInfo", "notes", "media", "cryptoKeys", "directories", "links", "relatedTo", "keywords", "members", "speakToAs", "calendars", "schedulingAddresses", "prodId", "created", "updated",
    ];

    /// <summary>A card as a plugin gets it: the id, the allowlisted properties, the flat uri views and, for a card of a shared account, isShared and originalId.</summary>
    internal static int Card(PluginValueBuilder b, JsonNode card)
    {
        var shared = card.IsTrue("_shared");
        var fields = new List<(string, int)> { ("id", b.String(card.Str("id") ?? "")) };
        foreach (var key in CardKeys)
        {
            if (card is JsonObject o && o[key] is { } value) fields.Add((key, Copy(b, value, 0)));
        }

        foreach (var (key, uri) in FlatUris(card))
        {
            fields.Add((key, b.String(uri)));
        }

        if (shared)
        {
            fields.Add(("isShared", b.Boolean(true)));
            if (card.Str("_rid") is { Length: > 0 } raw) fields.Add(("originalId", b.String(raw)));
        }

        return b.Object([.. fields]);
    }

    /// <summary>types.ts:550-572: the fields of a book a plugin sees; shareWith stays out.</summary>
    internal static int Book(PluginValueBuilder b, JsonNode book)
    {
        var fields = new List<(string, int)>
        {
            ("id", b.String(book.Str("id") ?? "")), ("name", b.String(book.Str("name") ?? "")),
            ("description", book.Str("description") is { } d ? b.String(d) : b.Null()),
            ("sortOrder", b.Number(book is JsonObject o && o["sortOrder"] is JsonValue v && v.TryGetValue<double>(out var sort) ? sort : 0)),
            ("isDefault", b.Boolean(book.IsTrue("isDefault"))), ("isSubscribed", b.Boolean(book.Child("isSubscribed") is not JsonValue s || !s.TryGetValue<bool>(out var subscribed) || subscribed)),
        };
        if (book.Child("myRights") is JsonObject rights)
        {
            fields.Add(("myRights", b.Object(("mayRead", b.Boolean(rights.IsTrue("mayRead"))), ("mayWrite", b.Boolean(rights.IsTrue("mayWrite"))),
                ("mayShare", b.Boolean(rights.IsTrue("mayShare"))), ("mayDelete", b.Boolean(rights.IsTrue("mayDelete"))))));
        }

        return b.Object([.. fields]);
    }

    /// <summary>contactFromWire (contact-wire.ts:107-119): the vCard CALURI, FBURL, CALADRURI and SOURCE views, only the ones the card has.</summary>
    internal static IEnumerable<(string Key, string Uri)> FlatUris(JsonNode card)
    {
        var calendars = card.Obj("calendars")?.Select(e => e.Value).OfType<JsonObject>().ToList() ?? [];
        if (calendars.FirstOrDefault(c => string.Equals(c.Str("kind"), "calendar", StringComparison.Ordinal))?.Str("uri") is { Length: > 0 } cal) yield return ("calendarUri", cal);
        if (calendars.FirstOrDefault(c => string.Equals(c.Str("kind"), "freeBusy", StringComparison.Ordinal))?.Str("uri") is { Length: > 0 } fb) yield return ("freeBusyUri", fb);
        if (card.Obj("schedulingAddresses")?.Select(e => e.Value).OfType<JsonObject>().FirstOrDefault()?.Str("uri") is { Length: > 0 } sched) yield return ("schedulingUri", sched);
        if (card.Obj("directories")?.Select(e => e.Value).OfType<JsonObject>().FirstOrDefault(d => string.Equals(d.Str("kind"), "entry", StringComparison.Ordinal))?.Str("uri") is { Length: > 0 } src) yield return ("source", src);
    }

    /// <summary>The JSON value as a plugin value; object keys that start with "_" (Barbican's own bookkeeping) and anything deeper than <see cref="MaxDepth"/> are left out.</summary>
    internal static int Copy(PluginValueBuilder b, JsonNode? node, int depth)
    {
        switch (node)
        {
            case JsonObject o:
                return depth >= MaxDepth ? b.Null() : b.Object([.. o.Where(p => !p.Key.StartsWith('_')).Select(p => (p.Key, Copy(b, p.Value, depth + 1)))]);
            case JsonArray a:
                return depth >= MaxDepth ? b.Null() : b.Array([.. a.Select(x => Copy(b, x, depth + 1))]);
            case JsonValue v:
                return v.GetValueKind() switch
                {
                    JsonValueKind.String => b.String(v.GetValue<string>()),
                    JsonValueKind.True => b.Boolean(true),
                    JsonValueKind.False => b.Boolean(false),
                    JsonValueKind.Number when v.TryGetValue<double>(out var number) => b.Number(number),
                    _ => b.Null(),
                };
            default:
                return b.Null();
        }
    }
}
