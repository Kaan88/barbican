// Mirrors: Bulwark lib/jmap/contact-wire.ts:1-105 contactToWire (the card as ContactCard/set takes it: client-only fields dropped, id / uid / addressBookIds never cleared by an update, the flat calendarUri, freeBusyUri,
// schedulingUri and source turned into their RFC 9553 maps, flat address fields turned into components) and host-api.ts:1036-1052 (contact.create takes a ContactCard, contact.update a Partial<ContactCard>).
// RFC 9553 section 2; Stalwart crates/jmap-proto/src/object/contact.rs (a property the card does not know fails the whole set). Barbican refuses what Bulwark would pass on to fail at the server: a property that is not
// one of the card's, a value of the wrong shape, a key that starts with "_", more than 5 000 values, nesting deeper than 12 levels or a string over 64 KB.
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal static class PluginContactInput
{
    internal const int MaxNodes = 5_000;
    internal const int MaxDepth = 12;
    internal const int MaxStringChars = 64 * 1024;

    /// <summary>Read back from list and get, and ignored when a plugin hands them in again (contact-wire.ts CLIENT_ONLY_KEYS and the server's own fields).</summary>
    private static readonly string[] Ignored = ["id", "originalId", "accountId", "accountName", "isShared", "localAccountId", "created", "updated", "@type", "version"];

    private static readonly string[] Strings = ["uid", "kind", "language", "prodId"];
    private static readonly string[] Flags = ["addressBookIds", "keywords", "members"];
    private static readonly string[] Objects = ["name", "speakToAs"];
    private static readonly string[] Maps =
    [
        "nicknames", "emails", "phones", "onlineServices", "preferredLanguages", "organizations", "titles", "addresses", "anniversaries", "personalInfo", "notes", "media", "cryptoKeys", "directories", "links",
        "relatedTo", "calendars", "schedulingAddresses",
    ];

    private static readonly string[] Flat = ["calendarUri", "freeBusyUri", "schedulingUri", "source"];

    private static readonly (string Field, string Kind)[] LegacyAddress = [("street", "name"), ("locality", "locality"), ("region", "region"), ("postcode", "postcode"), ("country", "country")];

    /// <summary>The card (create) or the changed properties (update; a null value clears one) as the writers take them; false for anything refused.</summary>
    internal static bool TryParse(PluginValue? value, bool create, out JsonObject card)
    {
        card = [];
        if (value is not { Kind: PluginValueKind.Object }) return false;
        var count = 0;
        foreach (var name in value.PropertyNames)
        {
            if (Array.IndexOf(Ignored, name) >= 0) continue;
            var property = value.Property(name);
            if (property.Kind == PluginValueKind.Undefined) continue;
            if (Array.IndexOf(Flat, name) >= 0)
            {
                if (property.Kind is not (PluginValueKind.String or PluginValueKind.Null) || (property.Kind == PluginValueKind.String && property.StringValue.Length > MaxStringChars)) return false;
                continue;
            }

            if (!Known(name)) return false;
            if (property.Kind == PluginValueKind.Null)
            {
                // an update clears a property with null; id, uid and addressBookIds are never cleared (contact-wire.ts:88); a create has nothing to clear
                if (!create && !(name is "uid" or "addressBookIds")) card[name] = null;
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(property.ToJson().GetRawText());
            }
            catch (Exception e) when (e is InvalidOperationException or JsonException)
            {
                return false;
            }

            if (node is null || !Shape(name, node) || !Bounded(node, 0, ref count)) return false;
            card[name] = node;
        }

        return Flatten(value, create, card);
    }

    private static bool Known(string name) => Array.IndexOf(Strings, name) >= 0 || Array.IndexOf(Flags, name) >= 0 || Array.IndexOf(Objects, name) >= 0 || Array.IndexOf(Maps, name) >= 0;

    private static bool Shape(string name, JsonNode node)
    {
        if (Array.IndexOf(Strings, name) >= 0) return node is JsonValue v && v.GetValueKind() == JsonValueKind.String;
        if (node is not JsonObject o) return false;
        if (Array.IndexOf(Flags, name) >= 0) return o.All(e => e.Value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False);
        return Array.IndexOf(Maps, name) < 0 || o.All(e => e.Value is JsonObject);
    }

    private static bool Bounded(JsonNode? node, int depth, ref int count)
    {
        if (depth > MaxDepth || ++count > MaxNodes) return false;
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o)
                {
                    if (key.StartsWith('_') || key.Length > MaxStringChars || !Bounded(child, depth + 1, ref count)) return false;
                }

                return true;
            case JsonArray a:
                foreach (var child in a)
                {
                    if (!Bounded(child, depth + 1, ref count)) return false;
                }

                return true;
            case JsonValue v:
                return v.GetValueKind() != JsonValueKind.String || v.GetValue<string>().Length <= MaxStringChars;
            default:
                return true;
        }
    }

    /// <summary>contact-wire.ts:51-105: addresses with flat fields get components; the flat uri fields become their RFC 9553 maps, only when the caller names them.</summary>
    private static bool Flatten(PluginValue input, bool create, JsonObject card)
    {
        if (card["addresses"] is JsonObject addresses)
        {
            foreach (var (id, address) in addresses.ToList())
            {
                if (address is JsonObject a) addresses[id] = Address(a);
            }
        }

        string? Uri(string key) => input.TryGetProperty(key, out var v) && v.Kind == PluginValueKind.String && v.StringValue.Length > 0 ? v.StringValue : null;
        if (input.TryGetProperty("calendarUri", out _) || input.TryGetProperty("freeBusyUri", out _))
        {
            var calendars = new JsonObject();
            if (Uri("calendarUri") is { } cal) calendars["cal"] = new JsonObject { ["@type"] = "Calendar", ["kind"] = "calendar", ["uri"] = cal };
            if (Uri("freeBusyUri") is { } fb) calendars["fb"] = new JsonObject { ["@type"] = "Calendar", ["kind"] = "freeBusy", ["uri"] = fb };
            if (calendars.Count > 0) card["calendars"] = calendars;
            else if (!create) card["calendars"] = null;
        }

        if (input.TryGetProperty("schedulingUri", out _))
        {
            if (Uri("schedulingUri") is { } sched) card["schedulingAddresses"] = new JsonObject { ["sched"] = new JsonObject { ["@type"] = "SchedulingAddress", ["uri"] = sched } };
            else if (!create) card["schedulingAddresses"] = null;
        }

        if (Uri("source") is { } source)
        {
            // vCard SOURCE is a JSContact directory entry (RFC 9553 section 2.6.2)
            var directories = card["directories"] is JsonObject given ? (JsonObject)given.DeepClone() : [];
            directories["source"] = new JsonObject { ["@type"] = "Directory", ["kind"] = "entry", ["uri"] = source };
            card["directories"] = directories;
        }

        return true;
    }

    private static JsonObject Address(JsonObject address)
    {
        var output = (JsonObject)address.DeepClone();
        foreach (var (field, _) in LegacyAddress) _ = output.Remove(field);
        if (address["components"] is not JsonArray { Count: > 0 })
        {
            var components = new JsonArray();
            foreach (var (field, kind) in LegacyAddress)
            {
                if (address[field] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Trim() is { Length: > 0 } text)
                {
                    components.Add((JsonNode?)new JsonObject { ["kind"] = kind, ["value"] = text });
                }
            }

            if (components.Count > 0)
            {
                output["components"] = components;
                output["isOrdered"] = true;
                output["defaultSeparator"] = ", ";
            }
        }

        return output;
    }
}
