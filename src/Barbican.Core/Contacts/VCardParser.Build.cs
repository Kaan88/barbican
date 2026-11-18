// One vCard's properties as a JSContact Card. Port of Rust vcard.rs:290 build (the same property table, the same skipped cards) with the per-kind entry caps of G-012.

#pragma warning disable MA0051 // one case per vCard property: the table of Rust vcard.rs build, read top to bottom

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

public static partial class VCardParser
{
    private static readonly (string Key, string Feature)[] TelFeatures =
    [
        ("CELL", "mobile"), ("MOBILE", "mobile"), ("VOICE", "voice"), ("FAX", "fax"), ("PAGER", "pager"), ("VIDEO", "video"), ("TEXT", "text"), ("TEXTPHONE", "textphone"), ("MAIN-NUMBER", "main-number"),
    ];

    private static readonly (string Key, string Service)[] XOnline =
    [
        ("X-AIM", "AIM"), ("X-ICQ", "ICQ"), ("X-JABBER", "Jabber"), ("X-MSN", "MSN"), ("X-MS-IMADDRESS", "Skype"), ("X-YAHOO", "Yahoo"), ("X-SKYPE", "Skype"), ("X-SKYPE-USERNAME", "Skype"),
        ("X-GADUGADU", "Gadu-Gadu"), ("X-GROUPWISE", "GroupWise"), ("X-GOOGLE-TALK", "Google Talk"), ("X-GOOGLETALK", "Google Talk"), ("X-TWITTER", "Twitter"), ("X-MASTODON", "Mastodon"),
        ("X-MATRIX", "Matrix"), ("X-TELEGRAM", "Telegram"), ("X-SIGNAL", "Signal"), ("X-DISCORD", "Discord"), ("X-QQ", "QQ"), ("X-WECHAT", "WeChat"), ("X-FACEBOOK", "Facebook"),
        ("X-INSTAGRAM", "Instagram"), ("X-LINKEDIN", "LinkedIn"),
    ];

    private static readonly (string Label, string Relation)[] RelationLabels =
    [
        ("spouse", "spouse"), ("wife", "spouse"), ("husband", "spouse"), ("partner", "sweetheart"), ("domestic partner", "sweetheart"), ("friend", "friend"), ("child", "child"), ("son", "child"),
        ("daughter", "child"), ("parent", "parent"), ("father", "parent"), ("mother", "parent"), ("brother", "sibling"), ("sister", "sibling"), ("sibling", "sibling"), ("relative", "kin"),
        ("assistant", "colleague"), ("manager", "colleague"), ("colleague", "colleague"), ("referred by", "contact"), ("kin", "kin"), ("sweetheart", "sweetheart"),
    ];

    private static readonly string[] Kinds = ["group", "individual", "org", "location", "device", "application"];

    private static readonly string[] PhotoTypes = ["JPEG", "JPG", "PNG", "GIF", "WEBP", "HEIC", "BMP", "SVG"];

    private static JsonObject? Contexts(string? type)
    {
        if (type is null)
        {
            return null;
        }

        var types = type.ToUpperInvariant().Split(',').Select(x => x.Trim()).ToList();
        var m = new JsonObject();
        if (types.Contains("WORK", StringComparer.Ordinal))
        {
            m["work"] = ContactJson.Yes();
        }

        if (types.Contains("HOME", StringComparer.Ordinal))
        {
            m["private"] = ContactJson.Yes();
        }

        return m.Count == 0 ? null : m;
    }

    private static JsonObject? PhoneFeatures(string? type)
    {
        if (type is null)
        {
            return null;
        }

        var m = new JsonObject();
        foreach (var x in type.ToUpperInvariant().Split(','))
        {
            foreach (var (key, feature) in TelFeatures)
            {
                if (string.Equals(key, x.Trim(), StringComparison.Ordinal))
                {
                    m[feature] = ContactJson.Yes();
                    break;
                }
            }
        }

        return m.Count == 0 ? null : m;
    }

    private static string? RelationFromLabel(string label)
    {
        var l = label.Trim().ToLowerInvariant();
        foreach (var (key, relation) in RelationLabels)
        {
            if (string.Equals(key, l, StringComparison.Ordinal))
            {
                return relation;
            }
        }

        return null;
    }

    private static string NextKey(JsonObject m, string prefix)
    {
        var i = m.Count;
        while (true)
        {
            var k = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!m.ContainsKey(k))
            {
                return k;
            }

            i++;
        }
    }

    private static void Push(JsonObject card, string map, string prefix, JsonNode value, int cap)
    {
        if (card[map] is not JsonObject m)
        {
            m = [];
            card[map] = m;
        }

        if (m.Count < cap)
        {
            m[NextKey(m, prefix)] = value;
        }
    }

    internal static bool HasScheme(string v) => v.Length > 0 && char.IsAsciiLetter(v[0]) && v.IndexOf(':', StringComparison.Ordinal) is var i and > 0 && v[..i].All(c => char.IsAsciiLetterOrDigit(c) || "+.-".Contains(c, StringComparison.Ordinal));

    private static void AddAnniversary(JsonObject card, string kind, string raw, Dictionary<string, string> p, int cap)
    {
        if (ParseDate(raw, p) is { } date)
        {
            Push(card, "anniversaries", "a", new JsonObject { ["@type"] = "Anniversary", ["kind"] = kind, ["date"] = date }, cap);
        }
    }

    private static JsonObject? Build(List<Prop> props, VCardLimits limits)
    {
        var cap = limits.MaxEntriesPerKind;

        // Apple grouped labels: item1.TEL + item1.X-ABLabel
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (g, k, v) in props)
        {
            if (g.Length > 0 && string.Equals(SplitQuoted(k, ';')[0], "X-ABLABEL", StringComparison.OrdinalIgnoreCase))
            {
                labels[g] = AppleLabel(DecodeValue(v));
            }
        }

        var card = new JsonObject { ["@type"] = "Card", ["version"] = "1.0" };
        string? birthPlace = null;
        string? deathPlace = null;
        string? maiden = null;
        JsonObject? name = null;
        foreach (var (group, key, rawValue) in props)
        {
            var segs = SplitQuoted(key, ';');
            var prop = segs[0].ToUpperInvariant();
            var p = ParseParams(string.Join(';', segs.Skip(1)));
            if (labels.TryGetValue(group, out var gl) && !p.ContainsKey("X-ABLABEL"))
            {
                p["X-ABLABEL"] = gl;
            }

            var pf = Pref(p);
            var qp = PStr(p, "ENCODING") is { } enc && enc.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
            var decoded = qp ? DecodeQuotedPrintable(rawValue) : rawValue;
            var val = DecodeValue(decoded);
            var label = PStr(p, "X-ABLABEL");
            var ty = PStr(p, "TYPE");
            var ctx = Contexts(ty);
            JsonNode? pfNode = pf is { } pn ? ContactJson.Num(pn) : null;
            switch (prop)
            {
                case "FN":
                    {
                        name ??= [];
                        if (!name.ContainsKey("components"))
                        {
                            var parts = val.Split(' ');
                            var comps = new JsonArray();
                            if (parts.Length >= 2)
                            {
                                comps.Append(new JsonObject { ["kind"] = "given", ["value"] = parts[0] });
                                comps.Append(new JsonObject { ["kind"] = "surname", ["value"] = string.Join(' ', parts.Skip(1)) });
                            }
                            else if (parts.Length == 1 && parts[0].Length > 0)
                            {
                                comps.Append(new JsonObject { ["kind"] = "given", ["value"] = parts[0] });
                            }

                            name["components"] = comps;
                            name["isOrdered"] = true;
                        }

                        if (!name.ContainsKey("full"))
                        {
                            name["full"] = val;
                        }

                        break;
                    }

                case "N":
                    {
                        var np = val.Split(';');
                        string G(int i) => i < np.Length ? np[i] : string.Empty;
                        var comps = new JsonArray();
                        foreach (var (i, kind) in new[] { (3, "title"), (1, "given"), (2, "given2"), (0, "surname"), (4, "generation") })
                        {
                            if (G(i).Length > 0)
                            {
                                comps.Append(new JsonObject { ["kind"] = kind, ["value"] = G(i) });
                            }
                        }

                        if (comps.Count > 0)
                        {
                            name ??= [];
                            name["components"] = comps;
                            name["isOrdered"] = true;
                        }

                        break;
                    }

                case "EMAIL":
                    Push(card, "emails", "e", ContactJson.Clean(new JsonObject { ["@type"] = "EmailAddress", ["address"] = StripScheme(val, "mailto"), ["contexts"] = ctx, ["label"] = label, ["pref"] = pfNode }), cap);
                    break;
                case "TEL":
                    Push(card, "phones", "p", ContactJson.Clean(new JsonObject { ["@type"] = "Phone", ["number"] = StripScheme(val, "tel"), ["contexts"] = ctx, ["features"] = PhoneFeatures(ty), ["label"] = label, ["pref"] = pfNode }), cap);
                    break;
                case "ORG":
                    {
                        var parts = val.Split(';').Where(x => x.Length > 0).ToList();
                        if (parts.Count > 0)
                        {
                            var o = new JsonObject { ["@type"] = "Organization", ["name"] = parts[0] };
                            if (parts.Count > 1)
                            {
                                o["units"] = new JsonArray([.. parts.Skip(1).Select(u => (JsonNode)new JsonObject { ["@type"] = "OrgUnit", ["name"] = u })]);
                            }

                            Push(card, "organizations", "o", o, cap);
                        }

                        break;
                    }

                case "ADR":
                    {
                        var ap = val.Split(';');
                        var comps = new JsonArray();
                        foreach (var (i, kind) in new[] { (2, "name"), (3, "locality"), (4, "region"), (5, "postcode"), (6, "country") })
                        {
                            if (i < ap.Length && ap[i].Length > 0)
                            {
                                comps.Append(new JsonObject { ["kind"] = kind, ["value"] = ap[i] });
                            }
                        }

                        var geo = PStr(p, "GEO");
                        Push(
                            card,
                            "addresses",
                            "a",
                            ContactJson.Clean(new JsonObject
                            {
                                ["@type"] = "Address",
                                ["components"] = comps,
                                ["countryCode"] = PStr(p, "CC"),
                                ["full"] = PStr(p, "LABEL"),
                                ["coordinates"] = geo is null ? null : StripScheme(geo, "geo"),
                                ["timeZone"] = PStr(p, "TZ"),
                                ["contexts"] = ctx,
                                ["label"] = label,
                                ["pref"] = pfNode,
                            }),
                            cap);
                        break;
                    }

                case "NOTE":
                    Push(card, "notes", "n", new JsonObject { ["@type"] = "Note", ["note"] = val }, cap);
                    break;
                case "NICKNAME":
                    foreach (var nick in val.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                    {
                        Push(card, "nicknames", "k", new JsonObject { ["@type"] = "Nickname", ["name"] = nick }, cap);
                    }

                    break;
                case "UID":
                    card["uid"] = val;
                    break;
                case "KIND" or "X-ADDRESSBOOKSERVER-KIND":
                    {
                        var k = val.ToLowerInvariant();
                        if (Kinds.Contains(k, StringComparer.Ordinal))
                        {
                            card["kind"] = k;
                        }

                        break;
                    }

                case "MEMBER" or "X-ADDRESSBOOKSERVER-MEMBER":
                    {
                        if (card["members"] is not JsonObject members)
                        {
                            members = [];
                            card["members"] = members;
                        }

                        if (members.Count < cap)
                        {
                            members[val.StartsWith("urn:uuid:", StringComparison.Ordinal) ? val[9..] : val] = true;
                        }

                        break;
                    }

                case "PHOTO" or "LOGO":
                    Media(card, string.Equals(prop, "LOGO", StringComparison.Ordinal) ? "logo" : "photo", p, rawValue, val, cap);
                    break;
                case "TITLE":
                    Push(card, "titles", "t", new JsonObject { ["@type"] = "Title", ["name"] = val, ["kind"] = "title" }, cap);
                    break;
                case "ROLE":
                    Push(card, "titles", "t", new JsonObject { ["@type"] = "Title", ["name"] = val, ["kind"] = "role" }, cap);
                    break;
                case "URL":
                    {
                        var tl = ty?.ToLowerInvariant();
                        var lab = label ?? (tl is "home" or "work" ? null : ty);
                        Push(card, "links", "l", ContactJson.Clean(new JsonObject { ["@type"] = "Link", ["uri"] = val, ["contexts"] = ctx, ["label"] = lab, ["pref"] = pfNode }), cap);
                        break;
                    }

                case "IMPP" or "X-SOCIALPROFILE" or "SOCIALPROFILE":
                    {
                        var s = new JsonObject { ["@type"] = "OnlineService", ["uri"] = val, ["contexts"] = ctx, ["pref"] = pfNode };
                        var service = PStr(p, "X-SERVICE-TYPE") ?? PStr(p, "SERVICE") ?? (!string.Equals(prop, "IMPP", StringComparison.Ordinal) && ty is not null && ty.ToLowerInvariant() is not ("home" or "work") ? ty : null);
                        if (service is not null)
                        {
                            s["service"] = service;
                        }

                        if (PStr(p, "X-USER") is { } user)
                        {
                            s["user"] = user;
                        }

                        if (label is not null)
                        {
                            s["label"] = label;
                        }

                        Push(card, "onlineServices", "s", ContactJson.Clean(s), cap);
                        break;
                    }

                case "BDAY":
                    AddAnniversary(card, "birth", val, p, cap);
                    break;
                case "ANNIVERSARY" or "X-ANNIVERSARY":
                    AddAnniversary(card, "wedding", val, p, cap);
                    break;
                case "DEATHDATE" or "X-DEATHDATE":
                    AddAnniversary(card, "death", val, p, cap);
                    break;
                case "X-ABDATE":
                    {
                        var l = (label ?? string.Empty).ToLowerInvariant();
                        var kind = l.Contains("birth", StringComparison.Ordinal) ? "birth" : l.Contains("anniversary", StringComparison.Ordinal) || l.Contains("wedding", StringComparison.Ordinal) ? "wedding" : "other";
                        AddAnniversary(card, kind, val, p, cap);
                        break;
                    }

                case "BIRTHPLACE":
                    birthPlace = val;
                    break;
                case "DEATHPLACE":
                    deathPlace = val;
                    break;
                case "CATEGORIES":
                    foreach (var c in val.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                    {
                        if (card["keywords"] is not JsonObject kw)
                        {
                            kw = [];
                            card["keywords"] = kw;
                        }

                        if (kw.Count < cap)
                        {
                            kw[c] = true;
                        }
                    }

                    break;
                case "RELATED":
                    {
                        var rel = new JsonObject();
                        if (ty is not null)
                        {
                            foreach (var x in ty.Split(','))
                            {
                                var n = x.Trim().ToLowerInvariant();
                                if (n.Length > 0)
                                {
                                    rel[n] = true;
                                }
                            }
                        }

                        var entry = new JsonObject { ["@type"] = "Relation" };
                        if (rel.Count > 0)
                        {
                            entry["relation"] = rel;
                        }

                        SetRelated(card, val, entry, cap);
                        break;
                    }

                case "X-ABRELATEDNAMES" or "X-SPOUSE" or "X-MANAGER" or "X-ASSISTANT":
                    {
                        var lab = prop switch { "X-SPOUSE" => "spouse", "X-MANAGER" => "manager", "X-ASSISTANT" => "assistant", _ => label ?? string.Empty };
                        if (val.Length > 0)
                        {
                            var entry = new JsonObject { ["@type"] = "Relation" };
                            if (RelationFromLabel(lab) is { } relation)
                            {
                                entry["relation"] = new JsonObject { [relation] = true };
                            }

                            SetRelated(card, val, entry, cap);
                        }

                        break;
                    }

                case "X-MAIDENNAME" or "X-BIRTHNAME":
                    maiden = val;
                    break;
                case "LANG":
                    Push(card, "preferredLanguages", "l", ContactJson.Clean(new JsonObject { ["@type"] = "LanguagePref", ["language"] = val, ["contexts"] = ctx, ["pref"] = pfNode }), cap);
                    break;
                default:
                    {
                        foreach (var (xk, svc) in XOnline)
                        {
                            if (!string.Equals(xk, prop, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var s = new JsonObject { ["@type"] = "OnlineService", ["uri"] = val, ["service"] = svc, ["contexts"] = ctx, ["pref"] = pfNode };
                            if (!HasScheme(val))
                            {
                                s["user"] = val;
                            }

                            if (label is not null)
                            {
                                s["label"] = label;
                            }

                            Push(card, "onlineServices", "s", ContactJson.Clean(s), cap);
                            break;
                        }

                        break;
                    }
            }
        }

        if (name is not null)
        {
            name["@type"] = "Name";
            card["name"] = name;
        }

        if (maiden is not null)
        {
            if (card["name"] is not JsonObject n)
            {
                n = new JsonObject { ["@type"] = "Name", ["components"] = new JsonArray(), ["isOrdered"] = true };
                card["name"] = n;
            }

            if (n["components"] is not JsonArray comps)
            {
                comps = [];
                n["components"] = comps;
            }

            if (!comps.Any(c => string.Equals(c.Str("kind"), "surname2", StringComparison.Ordinal)))
            {
                comps.Append(new JsonObject { ["kind"] = "surname2", ["value"] = maiden });
            }
        }

        foreach (var (kind, place) in new[] { ("birth", birthPlace), ("death", deathPlace) })
        {
            if (place is null)
            {
                continue;
            }

            if (card["anniversaries"] is not JsonObject a)
            {
                a = [];
                card["anniversaries"] = a;
            }

            var existing = a.FirstOrDefault(kv => string.Equals(kv.Value.Str("kind"), kind, StringComparison.Ordinal)).Value as JsonObject;
            var where = new JsonObject { ["@type"] = "Address", ["full"] = place };
            if (existing is not null)
            {
                existing["place"] = where;
            }
            else if (a.Count < cap)
            {
                a[NextKey(a, "a")] = new JsonObject { ["@type"] = "Anniversary", ["kind"] = kind, ["place"] = where };
            }
        }

        var hasName = card["name"] is JsonObject nm && ((nm["components"] is JsonArray nc && nc.Count > 0) || !string.IsNullOrEmpty(nm.Str("full")));
        var hasEmail = card["emails"] is JsonObject { Count: > 0 };
        var hasOrg = card["organizations"] is JsonObject orgs && orgs.FirstOrDefault().Value.Str("name") is { Length: > 0 };
        var isGroup = string.Equals(card.Str("kind"), "group", StringComparison.Ordinal);
        return hasName || hasEmail || hasOrg || isGroup ? card : null;
    }

    private static void SetRelated(JsonObject card, string key, JsonObject entry, int cap)
    {
        if (card["relatedTo"] is not JsonObject r)
        {
            r = [];
            card["relatedTo"] = r;
        }

        if (r.Count < cap || r.ContainsKey(key))
        {
            r[key] = entry;
        }
    }

    private static void Media(JsonObject card, string kind, Dictionary<string, string> p, string rawValue, string val, int cap)
    {
        var enc = PStr(p, "ENCODING")?.ToUpperInvariant();
        var t = PStr(p, "TYPE") ?? string.Empty;
        var mt = PStr(p, "MEDIATYPE") ?? (t.Contains('/', StringComparison.Ordinal) || PhotoTypes.Contains(t.ToUpperInvariant(), StringComparer.Ordinal) ? t : string.Empty);
        if (enc is "B" or "BASE64")
        {
            var mime = mt.Contains('/', StringComparison.Ordinal) ? mt : mt.Length > 0 ? "image/" + mt.ToLowerInvariant().Replace("jpg", "jpeg", StringComparison.Ordinal) : "image/jpeg";
            var data = new string([.. rawValue.Where(c => !char.IsWhiteSpace(c))]);
            if (!IsBase64Text(data))
            {
                return;
            }

            Push(card, "media", "m", new JsonObject { ["@type"] = "Media", ["kind"] = kind, ["uri"] = $"data:{mime};base64,{data}", ["mediaType"] = mime }, cap);
        }
        else if (val.StartsWith("data:", StringComparison.Ordinal))
        {
            // only a well-formed base64 data: URI is kept; anything else after "data:" is not an image
            if (ParseDataUri(val) is { } uri && IsBase64Text(uri.Data))
            {
                Push(card, "media", "m", ContactJson.Clean(new JsonObject { ["@type"] = "Media", ["kind"] = kind, ["uri"] = val, ["mediaType"] = mt.Contains('/', StringComparison.Ordinal) ? mt : null }), cap);
            }
        }
        else if (val.StartsWith("http://", StringComparison.Ordinal) || val.StartsWith("https://", StringComparison.Ordinal))
        {
            Push(card, "media", "m", ContactJson.Clean(new JsonObject { ["@type"] = "Media", ["kind"] = kind, ["uri"] = val, ["mediaType"] = mt.Contains('/', StringComparison.Ordinal) ? mt : null }), cap);
        }
    }
}
