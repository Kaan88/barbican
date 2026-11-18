// Mirrors: Rust vcard.rs:663 generate, :210 date_to_vcard, :89 encode_value; Bulwark lib/vcard.ts (generateVCard); RFC 6350 3.2 (folding at 75 octets). B-CON-017, B-CON-020. Contract from the M5 plan section 4.1; owner con-data.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>JSContact cards to vCard 4.0.</summary>
public static partial class VCardWriter
{
    /// <summary>One VCARD per card (VERSION:4.0, PRODID, UID, KIND, REV, N, FN, typed EMAIL and TEL with PREF, ADR, ANNIVERSARY and BDAY, RELATED, NOTE, CATEGORIES, ORG, URL, PHOTO from data: URIs), CRLF line ends, no line longer than 75 octets (folded with a space, never inside a UTF-8 sequence).</summary>
    public static string Generate(IReadOnlyList<JsonNode> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var sb = new StringBuilder();
        foreach (var card in cards)
        {
            foreach (var line in CardLines(card))
            {
                // a value with a line break would end the property and start another: a card from a server or a file must not forge lines (G-012)
                Fold(sb, Clean(line));
                _ = sb.Append("\r\n");
            }
        }

        return sb.ToString();
    }

    /// <summary>A PartialDate as vCard text: {month 4, day 12} is "--0412", a full date "19900102", a Timestamp its date part only.</summary>
    public static string DateToVCard(JsonNode date)
    {
        ArgumentNullException.ThrowIfNull(date);
        if (date.AsString() is { } text)
        {
            return text;
        }

        if (date.Str("utc") is { } utc)
        {
            var cut = utc.IndexOf('T', StringComparison.Ordinal);
            return (cut < 0 ? utc : utc[..cut]).Replace("-", string.Empty, StringComparison.Ordinal);
        }

        var y = ContactJson.Int(ContactJson.Get(date, "year"));
        var m = ContactJson.Int(ContactJson.Get(date, "month"));
        var d = ContactJson.Int(ContactJson.Get(date, "day"));
        static string Pad(long v, int width) => v.ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return (y, m, d) switch
        {
            ({ } yy, { } mm, { } dd) => Pad(yy, 4) + Pad(mm, 2) + Pad(dd, 2),
            (null, { } mm, { } dd) => "--" + Pad(mm, 2) + Pad(dd, 2),
            ({ } yy, { } mm, null) => Pad(yy, 4) + "-" + Pad(mm, 2),
            ({ } yy, null, null) => yy.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
    }

    private static string Clean(string line) => line.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

    /// <summary>RFC 6350 3.2: fold at 75 octets (the first line) and 74 (the rest, with their leading space) without splitting a UTF-8 character.</summary>
    private static void Fold(StringBuilder sb, string line)
    {
        var octets = 0;
        var first = true;
        foreach (var rune in line.EnumerateRunes())
        {
            var n = rune.Utf8SequenceLength;
            var limit = first ? 75 : 74;
            if (octets + n > limit)
            {
                _ = sb.Append("\r\n ");
                octets = 0;
                first = false;
            }

            _ = sb.Append(rune.ToString());
            octets += n;
        }
    }

    /// <summary>20250728T121700Z from 2025-07-28T12:17:00Z; text that is not a date and time is kept.</summary>
    private static string Timestamp(string v)
    {
        if (!DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            return v;
        }

        var zone = v.EndsWith('Z') || v.EndsWith('z') ? "Z" : (t.Offset < TimeSpan.Zero ? "-" : "+") + t.Offset.Duration().ToString("hhmm", CultureInfo.InvariantCulture);
        return t.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + zone;
    }
}
