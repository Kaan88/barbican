// Mirrors: Rust vcard.rs:262 parse, :73 decode_value, :183 parse_date, :519 address_parts, :654 parse_data_uri; Bulwark lib/vcard.ts (parseVCard); RFC 6350 (vCard 4.0), RFC 2426 (3.0), vCard 2.1; RFC 6868 (caret parameter encoding); RFC 9555 (JSContact vCard mapping).
// B-CON-018, B-CON-019, B-CON-021, B-CON-022; G-012. Contract from the M5 plan section 4.1; owner con-data. The reader is hand-written (D-M5-05); FolkerKinzel is not used.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>vCard text (2.1, 3.0, 4.0) to JSContact cards.</summary>
public static partial class VCardParser
{
    /// <summary>Every card of the file as a JSContact Card object (name components and full name, emails, phones, addresses, anniversaries, related people, notes, keywords from CATEGORIES, organizations, media as data: URIs, links, messaging), types and Apple grouped X-ABLabel kept, quoted-printable and RFC 6868 decoded, cards without any content skipped. Default <see cref="VCardLimits"/>.</summary>
    public static IReadOnlyList<JsonNode> Parse(string vcf)
    {
        ArgumentNullException.ThrowIfNull(vcf);
        var r = ParseChecked(vcf, new VCardLimits());
        return r.IsOk ? r.Value!.Cards : [];
    }

    /// <summary>Like <see cref="Parse"/> with explicit limits for a file the user picked: a file over the limit is a ValidationError, extra cards are counted as skipped, an oversized photo is dropped (the card stays), and nothing a hostile file contains makes the parser loop, recurse or allocate more than a few times the limits.</summary>
    public static Result<VCardParseResult> ParseChecked(string vcf, VCardLimits limits)
    {
        ArgumentNullException.ThrowIfNull(vcf);
        ArgumentNullException.ThrowIfNull(limits);
        if (vcf.Length > limits.MaxFileChars)
        {
            return Result<VCardParseResult>.Fail(new ValidationError("This file is too large to import as contacts."));
        }

        var cards = new List<JsonNode>();
        var skipped = 0;
        var dropped = 0;
        List<Prop>? cur = null;
        foreach (var logical in LogicalLines(vcf, limits))
        {
            if (logical.PhotoDropped)
            {
                if (cur is not null)
                {
                    dropped++;
                }

                continue;
            }

            var t = logical.Text.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (t.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                // a BEGIN inside a card starts over: the unfinished card is dropped (no recursion, no nesting)
                cur = [];
                continue;
            }

            if (t.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (cur is not null)
                {
                    FinishCard(cur, limits, cards, ref skipped);
                }

                cur = null;
                continue;
            }

            if (cur is not null && cur.Count < limits.MaxPropertiesPerCard && ValueColon(t) is { } ci and >= 1)
            {
                var (group, key) = SplitGroup(t[..ci]);
                cur.Add(new Prop(group, key, t[(ci + 1)..]));
            }
        }

        return Result<VCardParseResult>.Ok(new VCardParseResult(cards, skipped, dropped));
    }

    private static void FinishCard(List<Prop> props, VCardLimits limits, List<JsonNode> cards, ref int skipped)
    {
        if (cards.Count >= limits.MaxCards)
        {
            skipped++;
        }
        else if (Build(props, limits) is { } card)
        {
            cards.Add(card);
        }
    }

    /// <summary>"data:image/png;base64,AAA" as (media type, base64); an empty media type when it is missing; null for anything else.</summary>
    public static (string MediaType, string Data)? ParseDataUri(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = uri[5..];
        var comma = rest.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return null;
        }

        var head = rest[..comma];
        if (!string.Equals(head, "base64", StringComparison.Ordinal) && !head.EndsWith(";base64", StringComparison.Ordinal))
        {
            return null;
        }

        var type = head.EndsWith("base64", StringComparison.Ordinal) ? head[..^6] : head;
        type = type.TrimEnd(';');
        var semi = type.IndexOf(';', StringComparison.Ordinal);
        return (semi < 0 ? type : type[..semi], rest[(comma + 1)..]);
    }

    /// <summary>The five lines of a JSContact address: the flat keys win over the components (Rust vcard.rs:519 address_parts).</summary>
    public static AddressParts AddressPartsOf(JsonNode address)
    {
        ArgumentNullException.ThrowIfNull(address);
        string Comp(params string[] kinds) => string.Join(' ', ContactJson.Get(address, "components") is JsonArray a
            ? a.Select(x => (Kind: x.Str("kind"), Value: x.Str("value"))).Where(x => x.Kind is not null && kinds.Contains(x.Kind, StringComparer.Ordinal) && x.Value is not null).Select(x => x.Value!)
            : []);
        string Flat(string key) => address.Str(key) ?? string.Empty;
        static string Or(string a, string b) => a.Length == 0 ? b : a;
        return new AddressParts(
            Or(Flat("street"), Comp("number", "name", "block", "building", "floor", "apartment", "room", "extension", "direction", "landmark", "postOfficeBox", "subdistrict", "district")),
            Or(Flat("locality"), Comp("locality")),
            Or(Flat("region"), Comp("region")),
            Or(Flat("postcode"), Comp("postcode")),
            Or(Flat("country"), Comp("country")));
    }

    /// <summary>One property line: its group ("item1"), its name with parameters, its raw value.</summary>
    private readonly record struct Prop(string Group, string Key, string Value);
}
