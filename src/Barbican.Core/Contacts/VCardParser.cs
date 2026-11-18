// Mirrors: Rust vcard.rs:262 parse, :73 decode_value, :183 parse_date, :519 address_parts, :654 parse_data_uri; Bulwark lib/vcard.ts (parseVCard); RFC 6350 (vCard 4.0), RFC 2426 (3.0), vCard 2.1; RFC 6868 (caret parameter encoding); RFC 9555 (JSContact vCard mapping).
// B-CON-018, B-CON-019, B-CON-021, B-CON-022; G-012. Contract from the M5 plan section 4.1; owner con-data. The reader is hand-written (D-M5-05); FolkerKinzel is not used.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>vCard text (2.1, 3.0, 4.0) to JSContact cards.</summary>
public static class VCardParser
{
    /// <summary>Every card of the file as a JSContact Card object (name components and full name, emails, phones, addresses, anniversaries, related people, notes, keywords from CATEGORIES, organizations, media as data: URIs, links, messaging), types and Apple grouped X-ABLabel kept, quoted-printable and RFC 6868 decoded, cards without any content skipped. Default <see cref="VCardLimits"/>.</summary>
    public static IReadOnlyList<JsonNode> Parse(string vcf) => throw new PortPendingException("VCardParser.Parse");

    /// <summary>Like <see cref="Parse"/> with explicit limits for a file the user picked: a file over the limit is a ValidationError, extra cards are counted as skipped, an oversized photo is dropped (the card stays), and nothing a hostile file contains makes the parser loop, recurse or allocate more than a few times the limits.</summary>
    public static Result<VCardParseResult> ParseChecked(string vcf, VCardLimits limits) => throw new PortPendingException("VCardParser.ParseChecked");

    /// <summary>A vCard date value as a PartialDate: "1985-04-12", "19850412T232050Z", "--0412" (month and day only), "---12" (day only), "1985-04" (year and month); null for an invalid month or day or for text.</summary>
    public static JsonNode? ParseDate(string value, IReadOnlyDictionary<string, string> parameters) => throw new PortPendingException("VCardParser.ParseDate");

    /// <summary>"data:image/png;base64,AAA" as (media type, base64); an empty media type when it is missing; null for anything else.</summary>
    public static (string MediaType, string Data)? ParseDataUri(string uri) => throw new PortPendingException("VCardParser.ParseDataUri");

    public static AddressParts AddressPartsOf(JsonNode address) => throw new PortPendingException("VCardParser.AddressPartsOf");
}
