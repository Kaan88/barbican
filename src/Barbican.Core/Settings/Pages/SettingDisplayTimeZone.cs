// Mirrors: Bulwark lib/timezone.ts resolveTimeZone and the S-timeZone setting.
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Settings.Pages;

/// <summary>Resolves a display zone without changing the clock or stored instants.</summary>
public static class SettingDisplayTimeZone
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ZoneIds = new(() =>
        TimeZones.All().GroupBy(x => x, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));
    /// <summary>Returns false for automatic or unavailable zones, so callers can expose a fallback.</summary>
    public static bool TryResolve(string? id, out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(id) || string.Equals(id, "auto", StringComparison.OrdinalIgnoreCase)) return false;
        if (!ZoneIds.Value.TryGetValue(id, out var known)) return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(known);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    public static TimeZoneInfo Resolve(string? id, TimeZoneInfo fallback) => TryResolve(id, out var zone) ? zone! : fallback;

    /// <summary>The valid IANA choice for a new event, or the OS zone translated to IANA.</summary>
    public static string EventZone(string? id, TimeZoneInfo fallback)
    {
        if (id is not null && ZoneIds.Value.TryGetValue(id, out var known) && TryResolve(known, out _)) return known;
        var name = fallback.Id;
        if (TimeZones.All().Contains(name, StringComparer.Ordinal) || string.Equals(name, "UTC", StringComparison.Ordinal)) return name;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(name, out var iana) ? iana : "UTC";
    }
}
