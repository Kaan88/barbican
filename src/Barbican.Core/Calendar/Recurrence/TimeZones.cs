// Mirrors: Rust recurrence.rs:68 tz_of, :71 zone_to_local, :83 zone_to_utc_ms, :92 local_to_zone, :101 all_zones, :104 search_zones; Bulwark components/calendar/time-zone-picker.tsx. IANA names through NodaTime's tzdb.
// Contract from the M4 plan section 4.1; owner cal-data.

using NodaTime;

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>Time-zone conversion for event wall clocks. <c>local</c> is the user's zone (default: the machine's); a null or unknown <c>zone</c> means floating: the wall time is already local.</summary>
public static class TimeZones
{
    private static readonly Lazy<IReadOnlyList<string>> Names = new(() => [.. DateTimeZoneProviders.Tzdb.Ids.Order(StringComparer.Ordinal)]);

    /// <summary>The wall time in <paramref name="zone"/> as the same instant on the user's clock.</summary>
    public static DateTime ZoneToLocal(DateTime wall, string? zone, TimeZoneInfo? local = null)
    {
        if (ZoneOf(zone) is not { } z)
        {
            return wall;
        }

        var ms = ZoneInstantMs(wall, z) ?? new DateTimeOffset(Unspecified(wall), TimeSpan.Zero).ToUnixTimeMilliseconds();
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, local ?? TimeZoneInfo.Local), DateTimeKind.Unspecified);
    }

    /// <summary>The wall time in <paramref name="zone"/> as milliseconds since the Unix epoch (floating: the user's zone).</summary>
    public static long ZoneToUtcMs(DateTime wall, string? zone, TimeZoneInfo? local = null)
    {
        var utcFallback = new DateTimeOffset(Unspecified(wall), TimeSpan.Zero).ToUnixTimeMilliseconds();
        return ZoneOf(zone) is { } z ? ZoneInstantMs(wall, z) ?? utcFallback : LocalInstantMs(wall, local ?? TimeZoneInfo.Local) ?? utcFallback;
    }

    /// <summary>The inverse of <see cref="ZoneToLocal"/>.</summary>
    public static DateTime LocalToZone(DateTime local, string? zone, TimeZoneInfo? localZone = null)
    {
        if (ZoneOf(zone) is not { } z)
        {
            return local;
        }

        if (LocalInstantMs(local, localZone ?? TimeZoneInfo.Local) is not { } ms)
        {
            return local;
        }

        return Instant.FromUnixTimeMilliseconds(ms).InZone(z).LocalDateTime.ToDateTimeUnspecified();
    }

    /// <summary>The wall time in <paramref name="zone"/> at an instant (UTC milliseconds); a floating zone gives the user's wall time.</summary>
    public static DateTime UtcMsToZone(long ms, string? zone, TimeZoneInfo? local = null)
    {
        if (ZoneOf(zone) is { } z)
        {
            return Instant.FromUnixTimeMilliseconds(ms).InZone(z).LocalDateTime.ToDateTimeUnspecified();
        }

        var utc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, local ?? TimeZoneInfo.Local), DateTimeKind.Unspecified);
    }

    public static IReadOnlyList<string> All() => Names.Value;

    /// <summary>Up to <paramref name="max"/> zone names: city prefix first, then name prefix, then contains ("new york" finds America/New_York).</summary>
    public static IReadOnlyList<string> Search(string query, int max)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Trim().ToLowerInvariant().Replace(' ', '_');
        if (q.Length == 0)
        {
            return [];
        }

        var hits = new List<(int Rank, string Name)>();
        foreach (var z in All())
        {
            var l = z.ToLowerInvariant();
            var city = l[(l.LastIndexOf('/') + 1)..];
            if (city.StartsWith(q, StringComparison.Ordinal))
            {
                hits.Add((0, z));
            }
            else if (l.StartsWith(q, StringComparison.Ordinal))
            {
                hits.Add((1, z));
            }
            else if (l.Contains(q, StringComparison.Ordinal))
            {
                hits.Add((2, z));
            }
        }

        return [.. hits.OrderBy(h => h.Rank).ThenBy(h => h.Name, StringComparer.Ordinal).Take(max).Select(h => h.Name)];
    }

    /// <summary>The tzdb zone of an IANA name; null for a null, empty or unknown name (floating).</summary>
    internal static DateTimeZone? ZoneOf(string? name) => string.IsNullOrEmpty(name) ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(name);

    private static DateTime Unspecified(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Unspecified);

    /// <summary>The instant of a wall time in a tzdb zone: the earlier of two when it happens twice, the hour after it when a DST change skips it (Rust zone_to_utc_ms).</summary>
    private static long? ZoneInstantMs(DateTime wall, DateTimeZone zone)
    {
        var ldt = LocalDateTime.FromDateTime(Unspecified(wall));
        var map = zone.MapLocal(ldt);
        if (map.Count == 0)
        {
            map = zone.MapLocal(ldt.PlusHours(1));
            if (map.Count == 0)
            {
                return null;
            }
        }

        return map.First().ToInstant().ToUnixTimeMilliseconds();
    }

    /// <summary>The instant of a wall time on the user's clock (the earlier of two for a doubled hour); null in the hour a DST change skips.</summary>
    private static long? LocalInstantMs(DateTime wall, TimeZoneInfo local)
    {
        var w = Unspecified(wall);
        if (local.IsInvalidTime(w))
        {
            return null;
        }

        var offset = local.IsAmbiguousTime(w) ? local.GetAmbiguousTimeOffsets(w).Max() : local.GetUtcOffset(w);
        return new DateTimeOffset(w, offset).ToUnixTimeMilliseconds();
    }
}
