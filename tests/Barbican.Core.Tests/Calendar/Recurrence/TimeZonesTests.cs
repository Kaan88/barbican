// Ported from Rust src/recurrence.rs test :539 zones_convert_and_search (T-241). M4 stream cal-data.

using Barbican.Core.Calendar.Recurrence;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Recurrence;

public sealed class TimeZonesTests
{
    [Fact]
    [RustTest("recurrence::zones_convert_and_search")]
    public void Zones_convert_and_search()
    {
        var wall = Dt("2026-07-01T12:00:00");

        // UTC noon is 12:00 UTC whatever the local zone; round trip local -> zone -> local
        var utc = TimeZones.ZoneToUtcMs(wall, "UTC");
        Assert.Equal(new DateTimeOffset(wall, TimeSpan.Zero).ToUnixTimeMilliseconds(), utc);
        var local = TimeZones.ZoneToLocal(wall, "America/New_York");
        Assert.Equal(wall, TimeZones.LocalToZone(local, "America/New_York"));
        Assert.Equal(wall, TimeZones.ZoneToLocal(wall, null));
        Assert.Contains(TimeZones.Search("new york", 5), z => string.Equals(z, "America/New_York", StringComparison.Ordinal));
        Assert.Equal("Europe/Berlin", TimeZones.Search("berlin", 5)[0]);
    }
}
