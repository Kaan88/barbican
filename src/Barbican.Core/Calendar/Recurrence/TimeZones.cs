// Mirrors: Rust recurrence.rs:68 tz_of, :71 zone_to_local, :83 zone_to_utc_ms, :92 local_to_zone, :101 all_zones, :104 search_zones; Bulwark components/calendar/time-zone-picker.tsx. IANA names through NodaTime's tzdb.
// Contract from the M4 plan section 4.1; owner cal-data replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>Time-zone conversion for event wall clocks. <c>local</c> is the user's zone (default: the machine's); a null or unknown <c>zone</c> means floating: the wall time is already local.</summary>
public static class TimeZones
{
    /// <summary>The wall time in <paramref name="zone"/> as the same instant on the user's clock.</summary>
    public static DateTime ZoneToLocal(DateTime wall, string? zone, TimeZoneInfo? local = null) => throw new PortPendingException("TimeZones.ZoneToLocal");

    /// <summary>The wall time in <paramref name="zone"/> as milliseconds since the Unix epoch (floating: the user's zone).</summary>
    public static long ZoneToUtcMs(DateTime wall, string? zone, TimeZoneInfo? local = null) => throw new PortPendingException("TimeZones.ZoneToUtcMs");

    /// <summary>The inverse of <see cref="ZoneToLocal"/>.</summary>
    public static DateTime LocalToZone(DateTime local, string? zone, TimeZoneInfo? localZone = null) => throw new PortPendingException("TimeZones.LocalToZone");

    public static IReadOnlyList<string> All() => throw new PortPendingException("TimeZones.All");

    /// <summary>Up to <paramref name="max"/> zone names: city prefix first, then name prefix, then contains ("new york" finds America/New_York).</summary>
    public static IReadOnlyList<string> Search(string query, int max) => throw new PortPendingException("TimeZones.Search");
}
