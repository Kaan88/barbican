// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:70-95 (WHEN_RE, readWhen) and :611-616 (readInstant).
using System.Globalization;
using System.Text.RegularExpressions;

namespace Barbican.Core.Plugins.Api.Calendar;

internal static partial class PluginWhenParser
{
    /// <summary>host-api-calendar.ts:76-95: a real date (and time) in one of the three shapes.</summary>
    internal static bool TryRead(string? value, out PluginWhen when)
    {
        when = default;
        if (value is null) return false;
        Match m;
        try { m = Shape().Match(value); }
        catch (RegexMatchTimeoutException) { return false; }
        if (!m.Success) return false;
        var (y, mo, d) = (Number(m, "y"), Number(m, "mo"), Number(m, "d"));
        if (y < 1 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return false;
        string? time = null;
        if (m.Groups["h"].Success)
        {
            var (h, mi, s) = (Number(m, "h"), Number(m, "mi"), m.Groups["s"].Success ? Number(m, "s") : 0);
            if (h > 23 || mi > 59 || s > 59) return false;
            time = string.Create(CultureInfo.InvariantCulture, $"{h:00}:{mi:00}:{s:00}");
        }

        when = new PluginWhen(string.Create(CultureInfo.InvariantCulture, $"{y:0000}-{mo:00}-{d:00}"), time, m.Groups["z"].Success);
        return true;
    }

    /// <summary>An instant in UTC as JSCalendar's UTCDateTime (<c>2026-05-01T07:30:00Z</c>); a date, a wall clock or a missing <c>Z</c> is refused.</summary>
    internal static bool TryInstant(string? value, out string instant)
    {
        instant = "";
        if (!TryRead(value, out var when) || !when.Utc || when.Time is null) return false;
        instant = when.Date + "T" + when.Time + "Z";
        return true;
    }

    private static int Number(Match m, string group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex("^(?<y>[0-9]{4})-(?<mo>[0-9]{2})-(?<d>[0-9]{2})(?:T(?<h>[0-9]{2}):(?<mi>[0-9]{2})(?::(?<s>[0-9]{2}))?(?<z>Z)?)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Shape();
}
