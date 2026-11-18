// How duplicate cards become one (Rust contact_merge.rs:66-196; Bulwark lib/contacts/merge.ts): the newest card stays and gains every entry the others have and it lacks.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

public static partial class ContactMerge
{
    private static readonly string[] Multi =
    [
        "emails", "phones", "addresses", "onlineServices", "notes", "nicknames", "organizations", "titles", "anniversaries", "personalInfo", "relatedTo", "cryptoKeys", "links", "directories",
        "preferredLanguages", "calendars", "schedulingAddresses",
    ];

    private static readonly string[] Single = ["name", "language", "speakToAs"];

    /// <summary>Keeps the card with the newest <c>updated</c> (the first wins a tie or no date); the patch adds what the others have and the kept card lacks (emails and phones not already there case-insensitively, entries of every other map under a free key such as "p1m1", keywords, media) and takes a single field only when the kept card has none. Null for an empty list.</summary>
    public static ContactMergePlan? Plan(IReadOnlyList<JsonNode> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count == 0)
        {
            return null;
        }

        var order = cards.Select((c, i) => (Card: c, Index: i)).OrderByDescending(x => UpdatedAt(x.Card)).ThenBy(x => x.Index).Select(x => x.Card).ToList();
        var keep = order[0];
        var others = order.Skip(1).ToList();
        var result = (JsonObject)keep.DeepClone();
        foreach (var other in others)
        {
            Absorb(result, other);
        }

        var patch = new JsonObject();
        foreach (var key in Multi.Concat(Single).Concat(["keywords", "media"]))
        {
            var a = result[key];
            var b = ContactJson.Get(keep, key);
            if (!string.Equals(Stable(a), Stable(b), StringComparison.Ordinal))
            {
                patch[key] = a?.DeepClone();
            }
        }

        return new ContactMergePlan(keep, others, patch);
    }

    private static void Absorb(JsonObject result, JsonNode other)
    {
        var orgIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in Multi)
        {
            if (ContactJson.Get(other, key) is JsonObject { Count: > 0 } from)
            {
                AbsorbEntries(result, key, from, orgIds);
            }
        }

        AbsorbKeywords(result, other);
        foreach (var key in Single)
        {
            if (!HasValue(result[key]) && HasValue(ContactJson.Get(other, key)))
            {
                result[key] = ContactJson.Get(other, key)!.DeepClone();
            }
        }

        AbsorbPhoto(result, other);
    }

    /// <summary>Entries of one map the kept card lacks (by signature) are added under a free key; the organisation of a moved title follows.</summary>
    private static void AbsorbEntries(JsonObject result, string key, JsonObject from, Dictionary<string, string> orgIds)
    {
        var into = result[key] is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        var sigs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, e) in into)
        {
            if (e is not null)
            {
                sigs[Signature(key, e)] = k;
            }
        }

        var hasPref = into.Any(kv => kv.Value is JsonObject o && o["pref"] is not null);
        var changed = false;
        foreach (var (ek, e) in from)
        {
            if (e is null)
            {
                continue;
            }

            var sig = Signature(key, e);
            if (sig.Length > 0 && sigs.TryGetValue(sig, out var ex))
            {
                if (string.Equals(key, "organizations", StringComparison.Ordinal))
                {
                    orgIds[ek] = ex;
                }

                continue;
            }

            var copy = e.DeepClone();
            if (hasPref && copy is JsonObject co)
            {
                _ = co.Remove("pref");
            }

            if (string.Equals(key, "titles", StringComparison.Ordinal) && copy.Str("organizationId") is { } oid && orgIds.TryGetValue(oid, out var mapped) && copy is JsonObject ct)
            {
                ct["organizationId"] = mapped;
            }

            var nk = FreeKey(into, ek);
            if (string.Equals(key, "organizations", StringComparison.Ordinal))
            {
                orgIds[ek] = nk;
            }

            into[nk] = copy;
            sigs[sig] = nk;
            changed = true;
        }

        if (changed)
        {
            result[key] = into;
        }
    }

    private static void AbsorbKeywords(JsonObject result, JsonNode other)
    {
        if (ContactJson.Get(other, "keywords") is not JsonObject kw)
        {
            return;
        }

        foreach (var (k, on) in kw)
        {
            if (ContactJson.IsTrue(on) && !ContactJson.IsTrue(ContactJson.Get(result["keywords"], k)))
            {
                if (result["keywords"] is not JsonObject target)
                {
                    target = [];
                    result["keywords"] = target;
                }

                target[k] = true;
            }
        }
    }

    private static void AbsorbPhoto(JsonObject result, JsonNode other)
    {
        var hasPhoto = ContactJson.Values(result, "media").Any(m => string.Equals(m.Str("kind"), "photo", StringComparison.Ordinal));
        if (hasPhoto || ContactJson.Get(other, "media") is not JsonObject media)
        {
            return;
        }

        foreach (var (k, m) in media)
        {
            if (m is not null && string.Equals(m.Str("kind"), "photo", StringComparison.Ordinal))
            {
                if (result["media"] is not JsonObject target)
                {
                    target = [];
                    result["media"] = target;
                }

                target[FreeKey(target, k)] = m.DeepClone();
                return;
            }
        }
    }

    private static string FreeKey(JsonObject m, string key)
    {
        if (!m.ContainsKey(key))
        {
            return key;
        }

        for (var n = 1; ; n++)
        {
            var k = $"{key}m{n.ToString(CultureInfo.InvariantCulture)}";
            if (!m.ContainsKey(k))
            {
                return k;
            }
        }
    }

    private static long UpdatedAt(JsonNode c) => c.Str("updated") is { } u && DateTimeOffset.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToUnixTimeMilliseconds() : long.MinValue;

    private static bool HasValue(JsonNode? v) => v switch
    {
        null => false,
        JsonValue s when s.GetValueKind() == JsonValueKind.String => s.GetValue<string>().Trim().Length > 0,
        JsonArray a => a.Count > 0,
        JsonObject o when o.ContainsKey("components") || o.ContainsKey("full") => (o["components"] is JsonArray { Count: > 0 }) || o["full"].AsString() is { } f && f.Trim().Length > 0,
        JsonObject o => o.Count > 0,
        _ => true,
    };

    /// <summary>JSON with sorted keys and without <c>pref</c> and nulls, to compare entries.</summary>
    private static string Stable(JsonNode? v) => v switch
    {
        null => "null",
        JsonArray a => "[" + string.Join(',', a.Select(Stable)) + "]",
        JsonObject o => "{" + string.Join(',', o.Where(kv => kv.Value is not null && !string.Equals(kv.Key, "pref", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => JsonValue.Create(kv.Key).ToJsonString() + ":" + Stable(kv.Value))) + "}",
        _ => v.ToJsonString(),
    };

    private static string AddressText(JsonNode e)
    {
        var p = VCardParser.AddressPartsOf(e);
        var t = string.Join(' ', new[] { p.Street, p.Locality, p.Region, p.Postcode, p.Country }.Where(x => x.Length > 0));
        return ContactText.Fold(t.Length == 0 ? e.Str("full") ?? e.Str("fullAddress") ?? string.Empty : t);
    }

    private static string Signature(string key, JsonNode e)
    {
        var s = (string k) => e.Str(k) ?? string.Empty;
        switch (key)
        {
            case "emails":
                return s("address").Trim().ToLowerInvariant();
            case "phones":
                {
                    var k = PhoneKey(s("number"));
                    return k.Length == 0 ? new string([.. s("number").Where(c => !char.IsWhiteSpace(c))]) : k;
                }

            case "addresses":
                return AddressText(e);
            case "onlineServices":
                return (e.Str("uri") ?? e.Str("user") ?? string.Empty).Trim().ToLowerInvariant();
            case "notes":
                return s("note").Trim();
            case "nicknames" or "titles":
                return ContactText.Fold(s("name"));
            case "organizations":
                return ContactText.Fold(s("name") + " " + Stable(ContactJson.Get(e, "units") ?? new JsonArray()));
            default:
                return Stable(e);
        }
    }
}
