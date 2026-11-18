// Mirrors: Rust vcard.rs:663 generate, :210 date_to_vcard, :89 encode_value; Bulwark lib/vcard.ts (generateVCard); RFC 6350 3.2 (folding at 75 octets). B-CON-017, B-CON-020. Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>JSContact cards to vCard 4.0.</summary>
public static class VCardWriter
{
    /// <summary>One VCARD per card (VERSION:4.0, PRODID, UID, KIND, REV, N, FN, typed EMAIL and TEL with PREF, ADR, ANNIVERSARY and BDAY, RELATED, NOTE, CATEGORIES, ORG, URL, PHOTO from data: URIs), CRLF line ends, no line longer than 75 octets (folded with a space, never inside a UTF-8 sequence).</summary>
    public static string Generate(IReadOnlyList<JsonNode> cards) => throw new PortPendingException("VCardWriter.Generate");

    /// <summary>A PartialDate as vCard text: {month 4, day 12} is "--0412", a full date "19900102", a Timestamp its date part only.</summary>
    public static string DateToVCard(JsonNode date) => throw new PortPendingException("VCardWriter.DateToVCard");
}
