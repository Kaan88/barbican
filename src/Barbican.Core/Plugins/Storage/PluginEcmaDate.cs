// Source: Bulwark lib/plugin-sandbox/host-api.ts:427-445 (storage JSON.stringify Date.toJSON).
// Gregorian arithmetic retains JavaScript's full TimeClip range, including year zero and signed extended years.
using System.Globalization;
namespace Barbican.Core.Plugins.Persistence;
internal static class PluginEcmaDate
{
    public static string? Format(double milliseconds)
    {
        if (double.IsNaN(milliseconds)) return null;
        if (!double.IsFinite(milliseconds) || Math.Abs(milliseconds) > 8_640_000_000_000_000d || Math.Truncate(milliseconds) != milliseconds)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        const long perDay = 86_400_000;
        var days = Math.DivRem((long)milliseconds, perDay, out var time);
        if (time < 0) { days--; time += perDay; }
        var (year, month, day) = CivilDate(days);
        var yearText = year is >= 0 and <= 9999 ? year.ToString("D4", CultureInfo.InvariantCulture)
            : (year < 0 ? "-" : "+") + Math.Abs(year).ToString("D6", CultureInfo.InvariantCulture);
        return FormattableString.Invariant($"{yearText}-{month:D2}-{day:D2}T{time / 3_600_000:D2}:{time / 60_000 % 60:D2}:{time / 1000 % 60:D2}.{time % 1000:D3}Z");
    }
    private static (long Year, long Month, long Day) CivilDate(long days)
    {
        // Work in March-based 400-year eras, with floor division for years before the epoch.
        var shifted = days + 719_468;
        var era = (shifted >= 0 ? shifted : shifted - 146_096) / 146_097;
        var dayOfEra = shifted - (era * 146_097);
        var yearOfEra = (dayOfEra - (dayOfEra / 1460) + (dayOfEra / 36_524) - (dayOfEra / 146_096)) / 365;
        var year = yearOfEra + (era * 400);
        var dayOfYear = dayOfEra - ((365 * yearOfEra) + (yearOfEra / 4) - (yearOfEra / 100));
        var marchMonth = ((5 * dayOfYear) + 2) / 153;
        var day = dayOfYear - (((153 * marchMonth) + 2) / 5) + 1;
        var month = marchMonth + (marchMonth < 10 ? 3 : -9);
        return (year + (month <= 2 ? 1 : 0), month, day);
    }
}
