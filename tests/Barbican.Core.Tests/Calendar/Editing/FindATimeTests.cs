// Ported from Rust src/cal_editor.rs test :1654 free_slots_skip_busy_and_off_hours (T-041). M4 stream cal-edit.

using System.Globalization;
using Barbican.Core.Calendar.Editing;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class FindATimeTests
{
    [Fact]
    [RustTest("cal_editor::free_slots_skip_busy_and_off_hours")]
    public void Free_slots_skip_busy_and_off_hours()
    {
        // Monday 2026-11-16 08:10 local
        var now = Dt("2026-11-16T08:10:00");
        var busy = new[] { (LocalMs("2026-11-16T08:30:00"), LocalMs("2026-11-16T10:00:00")) };
        var slots = FindATime.FindFreeSlots(now, 3_600_000, [1, 2, 3, 4, 5], 8, 20, busy, 3);
        Assert.Equal(["16 10:00", "16 10:30", "16 11:00"], slots.Select(s => s.Start.ToString("dd HH:mm", CultureInfo.InvariantCulture)).ToArray());

        // the last slot of a day ends by its end hour; weekends are skipped
        var late = Dt("2026-11-20T19:10:00");
        var s = FindATime.FindFreeSlots(late, 3_600_000, [1, 2, 3, 4, 5], 8, 20, [], 1);
        Assert.Equal(Dt("2026-11-23T08:00:00"), s[0].Start);
        Assert.Equal(new AvailabilityStatus("Tentative", 2), FindATime.AvailabilityFor([(0, 10, true)], 5, 20));
        Assert.Equal(new AvailabilityStatus("Busy", 3), FindATime.AvailabilityFor([(0, 10, true), (12, 30, false)], 5, 20));
        Assert.Equal(new AvailabilityStatus("Free", 1), FindATime.AvailabilityFor([(30, 40, false)], 5, 20));
    }
}
