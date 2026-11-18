// A vCard DATE or DATE-AND-OR-TIME as a JSContact PartialDate (time dropped). Port of Rust vcard.rs:183 parse_date; B-CON-019.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

public static partial class VCardParser
{
    /// <summary>A vCard date value as a PartialDate: "1985-04-12", "19850412T232050Z", "--0412" (month and day only), "---12" (day only), "1985-04" (year and month); null for an invalid month or day or for text.</summary>
    public static JsonNode? ParseDate(string value, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(parameters);
        if (PStr(parameters, "VALUE") is { } kind && kind.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var d = value.Trim().Split('T', ' ')[0];
        static bool Digits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
        static long N(string s) => long.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);

        if (d.StartsWith("---", StringComparison.Ordinal))
        {
            var r = d[3..];
            return r.Length == 2 && Digits(r) ? Partial(null, null, N(r)) : null;
        }

        if (d.StartsWith("--", StringComparison.Ordinal))
        {
            var r = d[2..];
            var r2 = r.Replace("-", string.Empty, StringComparison.Ordinal);
            if (r2.Length == 4 && Digits(r2))
            {
                return Partial(null, N(r2[..2]), N(r2[2..]));
            }

            return r2.Length == 2 && Digits(r2) && !r.Contains('-', StringComparison.Ordinal) ? Partial(null, N(r2), null) : null;
        }

        var compact = d.Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length == 8 && Digits(compact) && (d.Length == 8 || d.Length == 10))
        {
            return Partial(N(compact[..4]), N(compact[4..6]), N(compact[6..]));
        }

        if (d.Length == 7 && d[4] == '-' && Digits(d[..4]) && Digits(d[5..]))
        {
            return Partial(N(d[..4]), N(d[5..]), null);
        }

        return d.Length == 4 && Digits(d) ? Partial(N(d), null, null) : null;
    }

    private static JsonObject? Partial(long? year, long? month, long? day)
    {
        if (month is { } m && (m < 1 || m > 12))
        {
            return null;
        }

        if (day is { } dd && (dd < 1 || dd > 31))
        {
            return null;
        }

        var o = new JsonObject { ["@type"] = "PartialDate" };
        if (year is { } y)
        {
            o["year"] = ContactJson.Num(y);
        }

        if (month is { } mo)
        {
            o["month"] = ContactJson.Num(mo);
        }

        if (day is { } da)
        {
            o["day"] = ContactJson.Num(da);
        }

        return o.Count > 1 ? o : null;
    }
}
