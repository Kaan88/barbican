// The property lines of one card, in the order of Rust vcard.rs:217 one (and Bulwark generateVCard).

#pragma warning disable MA0051 // one block per card property, in the order of Rust vcard.rs one

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

public static partial class VCardWriter
{
    private static string ContextType(JsonNode? contexts) => ContactJson.IsTrue(ContactJson.Get(contexts, "work")) ? "WORK" : ContactJson.IsTrue(ContactJson.Get(contexts, "private")) ? "HOME" : string.Empty;

    private static string ParamsText(List<string> p) => p.Count == 0 ? string.Empty : ";" + string.Join(';', p);

    private static string E(string? v) => VCardParser.EncodeValue(v ?? string.Empty);

    private static string FeatureType(string key)
    {
        var kl = key.ToLowerInvariant();
        if (kl is "mobile" or "cell")
        {
            return "CELL";
        }

        foreach (var (type, feature) in new[] { ("VOICE", "voice"), ("FAX", "fax"), ("PAGER", "pager"), ("VIDEO", "video"), ("TEXT", "text"), ("TEXTPHONE", "textphone"), ("MAIN-NUMBER", "main-number") })
        {
            if (string.Equals(feature, kl, StringComparison.Ordinal))
            {
                return type;
            }
        }

        return kl.StartsWith("x-", StringComparison.Ordinal) && kl[2..].All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? key.ToUpperInvariant() : string.Empty;
    }

    private static void CommonParams(List<string> p, JsonNode entry, bool withLabel)
    {
        if (ContextType(ContactJson.Get(entry, "contexts")) is { Length: > 0 } t)
        {
            p.Add("TYPE=" + t);
        }

        if (ContactJson.Int(ContactJson.Get(entry, "pref")) is { } n)
        {
            p.Add("PREF=" + n.ToString(CultureInfo.InvariantCulture));
        }

        if (withLabel && entry.Str("label") is { Length: > 0 } label)
        {
            p.Add("X-ABLABEL=\"" + VCardParser.EncodeParam(label) + "\"");
        }
    }

    private static List<string> CardLines(JsonNode? c)
    {
        var l = new List<string> { "BEGIN:VCARD", "VERSION:4.0", "PRODID:-//Barbican//Barbican Mail//EN" };
        if (c.Str("uid") is { } uid)
        {
            l.Add(VCardParser.HasScheme(uid) ? $"UID:{uid}" : $"UID;VALUE=text:{uid}");
        }

        if (c.Str("kind") is { } kind)
        {
            l.Add($"KIND:{kind}");
        }

        if (c.Str("updated") is { } updated)
        {
            l.Add($"REV:{Timestamp(updated)}");
        }

        Names(l, c);
        foreach (var n in ContactJson.Values(c, "nicknames"))
        {
            if (n.Str("name") is { } x)
            {
                l.Add($"NICKNAME:{E(x)}");
            }
        }

        foreach (var e in ContactJson.Values(c, "emails"))
        {
            var p = new List<string>();
            CommonParams(p, e, true);
            l.Add($"EMAIL{ParamsText(p)}:{e.Str("address") ?? string.Empty}");
        }

        foreach (var ph in ContactJson.Values(c, "phones"))
        {
            var types = new List<string>();
            if (ContextType(ContactJson.Get(ph, "contexts")) is { Length: > 0 } ctx)
            {
                types.Add(ctx);
            }

            if (ContactJson.Get(ph, "features") is JsonObject features)
            {
                foreach (var (k, on) in features)
                {
                    if (ContactJson.IsTrue(on) && FeatureType(k) is { Length: > 0 } t && !types.Contains(t, StringComparer.Ordinal))
                    {
                        types.Add(t);
                    }
                }
            }

            var p = new List<string>();
            if (types.Count > 0)
            {
                p.Add("TYPE=" + string.Join(',', types));
            }

            if (ContactJson.Int(ContactJson.Get(ph, "pref")) is { } pref)
            {
                p.Add("PREF=" + pref.ToString(CultureInfo.InvariantCulture));
            }

            if (ph.Str("label") is { Length: > 0 } label)
            {
                p.Add("X-ABLABEL=\"" + VCardParser.EncodeParam(label) + "\"");
            }

            l.Add($"TEL{ParamsText(p)}:{ph.Str("number") ?? string.Empty}");
        }

        foreach (var o in ContactJson.Values(c, "organizations"))
        {
            var parts = new List<string> { o.Str("name") ?? string.Empty };
            if (ContactJson.Get(o, "units") is JsonArray units)
            {
                parts.AddRange(units.Select(u => u.Str("name")).OfType<string>());
            }

            l.Add("ORG:" + string.Join(';', parts.Select(E)));
        }

        foreach (var t in ContactJson.Values(c, "titles"))
        {
            var n = E(t.Str("name"));
            l.Add(string.Equals(t.Str("kind"), "role", StringComparison.Ordinal) ? $"ROLE:{n}" : $"TITLE:{n}");
        }

        Addresses(l, c);
        Dates(l, c);
        Services(l, c);
        More(l, c);
        l.Add("END:VCARD");
        return l;
    }

    private static void Names(List<string> l, JsonNode? c)
    {
        var comps = ContactJson.Get(ContactJson.Get(c, "name"), "components") as JsonArray ?? [];
        string Find(params string[] kinds) => comps.Select(x => (Kind: x.Str("kind"), Value: x.Str("value"))).FirstOrDefault(x => x.Kind is not null && kinds.Contains(x.Kind, StringComparer.Ordinal)).Value ?? string.Empty;
        var given = Find("given");
        var surname = Find("surname");
        var prefix = Find("title", "prefix");
        var suffix = Find("generation", "suffix");
        var add = Find("given2", "additional", "middle");
        var joined = string.Join(' ', new[] { prefix, given, add, surname, suffix }.Where(x => x.Length > 0));
        var full = joined.Length > 0
            ? joined
            : ContactJson.Get(c, "name").Str("full") ?? ContactJson.Values(c, "organizations").Select(o => o.Str("name")).FirstOrDefault() ?? string.Empty;
        if (full.Length > 0)
        {
            l.Add($"N:{E(surname)};{E(given)};{E(add)};{E(prefix)};{E(suffix)}");
            l.Add($"FN:{E(full)}");
        }

        if (Find("surname2") is { Length: > 0 } s2)
        {
            l.Add($"X-MAIDENNAME:{E(s2)}");
        }
    }

    private static void Addresses(List<string> l, JsonNode? c)
    {
        foreach (var a in ContactJson.Values(c, "addresses"))
        {
            var p = new List<string>();
            if (ContextType(ContactJson.Get(a, "contexts")) is { Length: > 0 } t)
            {
                p.Add("TYPE=" + t);
            }

            if (a.Str("countryCode") is { } cc)
            {
                p.Add("CC=" + cc);
            }

            if (ContactJson.Int(ContactJson.Get(a, "pref")) is { } pref)
            {
                p.Add("PREF=" + pref.ToString(CultureInfo.InvariantCulture));
            }

            if (a.Str("label") is { Length: > 0 } label)
            {
                p.Add("X-ABLABEL=\"" + VCardParser.EncodeParam(label) + "\"");
            }

            var parts = VCardParser.AddressPartsOf(a);
            var street = parts.Street;
            if (parts.Street.Length == 0 && parts.Locality.Length == 0 && parts.Region.Length == 0 && parts.Postcode.Length == 0 && parts.Country.Length == 0)
            {
                street = a.Str("full") ?? a.Str("fullAddress") ?? string.Empty;
            }

            l.Add($"ADR{ParamsText(p)}:;;{E(street)};{E(parts.Locality)};{E(parts.Region)};{E(parts.Postcode)};{E(parts.Country)}");
        }
    }

    private static void Dates(List<string> l, JsonNode? c)
    {
        foreach (var an in ContactJson.Values(c, "anniversaries"))
        {
            var date = ContactJson.Get(an, "date");
            var d = date is null ? string.Empty : DateToVCard(date);
            var place = ContactJson.Get(an, "place").Str("full") ?? ContactJson.Get(an, "place").Str("fullAddress");
            switch (an.Str("kind") ?? string.Empty)
            {
                case "birth":
                    if (d.Length > 0)
                    {
                        l.Add($"BDAY:{d}");
                    }

                    if (place is not null)
                    {
                        l.Add($"BIRTHPLACE:{E(place)}");
                    }

                    break;
                case "wedding":
                    if (d.Length > 0)
                    {
                        l.Add($"ANNIVERSARY:{d}");
                    }

                    break;
                case "death":
                    if (d.Length > 0)
                    {
                        l.Add($"DEATHDATE:{d}");
                    }

                    if (place is not null)
                    {
                        l.Add($"DEATHPLACE:{E(place)}");
                    }

                    break;
                default:
                    if (d.Length > 0)
                    {
                        l.Add($"X-ABDATE:{d}");
                    }

                    break;
            }
        }
    }

    private static void Services(List<string> l, JsonNode? c)
    {
        foreach (var s in ContactJson.Values(c, "onlineServices"))
        {
            var p = new List<string>();
            if (s.Str("service") is { } sv)
            {
                p.Add("X-SERVICE-TYPE=" + sv);
            }

            CommonParams(p, s, false);
            var uri = s.Str("uri") ?? s.Str("user") ?? string.Empty;
            l.Add(s.Str("service") is not null || s.Str("user") is not null ? $"IMPP{ParamsText(p)}:{uri}" : $"URL{ParamsText(p)}:{uri}");
        }

        foreach (var k in ContactJson.Values(c, "links"))
        {
            var p = new List<string>();
            CommonParams(p, k, true);
            l.Add($"{(string.Equals(k.Str("kind"), "contact", StringComparison.Ordinal) ? "CONTACT-URI" : "URL")}{ParamsText(p)}:{k.Str("uri") ?? string.Empty}");
        }
    }

    private static void More(List<string> l, JsonNode? c)
    {
        if (ContactJson.Get(c, "keywords") is JsonObject kw)
        {
            var cats = kw.Where(kv => ContactJson.IsTrue(kv.Value)).Select(kv => E(kv.Key)).ToList();
            if (cats.Count > 0)
            {
                l.Add("CATEGORIES:" + string.Join(',', cats));
            }
        }

        foreach (var lang in ContactJson.Values(c, "preferredLanguages"))
        {
            var p = new List<string>();
            CommonParams(p, lang, false);
            l.Add($"LANG{ParamsText(p)}:{lang.Str("language") ?? string.Empty}");
        }

        if (ContactJson.Get(c, "relatedTo") is JsonObject related)
        {
            foreach (var (uri, rel) in related)
            {
                var p = new List<string>();
                var scheme = VCardParser.HasScheme(uri);
                if (!scheme)
                {
                    p.Add("VALUE=text");
                }

                if (ContactJson.FirstTrueKey(ContactJson.Get(rel, "relation")) is { } type)
                {
                    p.Add("TYPE=" + type);
                }

                l.Add($"RELATED{ParamsText(p)}:{(scheme ? uri : E(uri))}");
            }
        }

        foreach (var n in ContactJson.Values(c, "notes"))
        {
            if (n.Str("note") is { } note)
            {
                l.Add($"NOTE:{E(note)}");
            }
        }

        if (ContactJson.Get(c, "members") is JsonObject members)
        {
            foreach (var (k, on) in members)
            {
                if (ContactJson.IsTrue(on))
                {
                    l.Add($"MEMBER:{(VCardParser.HasScheme(k) ? k : "urn:uuid:" + k)}");
                }
            }
        }

        Media(l, c);
        foreach (var a in ContactJson.Values(c, "addresses"))
        {
            if (a.Str("coordinates") is { } geo)
            {
                l.Add("GEO:" + (geo.StartsWith("geo:", StringComparison.OrdinalIgnoreCase) ? geo : "geo:" + geo.Replace(';', ',')));
            }

            if (a.Str("timeZone") is { } tz)
            {
                l.Add($"TZ:{tz}");
            }
        }

        if (c.Str("created") is { } created)
        {
            l.Add($"CREATED:{Timestamp(created)}");
        }
    }

    private static void Media(List<string> l, JsonNode? c)
    {
        foreach (var m in ContactJson.Values(c, "media"))
        {
            if (m.Str("uri") is not { } uri)
            {
                continue;
            }

            var prop = m.Str("kind") switch { "logo" => "LOGO", "sound" => "SOUND", _ => "PHOTO" };
            if (VCardParser.ParseDataUri(uri) is { } parsed)
            {
                var type = parsed.MediaType.Length == 0 ? m.Str("mediaType") ?? string.Empty : parsed.MediaType;
                l.Add($"{prop}:data:{type};base64,{parsed.Data}");
            }
            else if (!uri.StartsWith("data:", StringComparison.Ordinal))
            {
                var mt = m.Str("mediaType") is { } x ? $";MEDIATYPE={x}" : string.Empty;
                l.Add($"{prop};VALUE=URI{mt}:{uri}");
            }
        }
    }
}
