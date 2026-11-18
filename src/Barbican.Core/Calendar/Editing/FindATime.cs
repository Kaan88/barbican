// Mirrors: Rust cal_editor.rs:1247 availability_for, :1294-1296 SEARCH_WORKING_DAYS, MAX_SUGGESTIONS, GRID_MINUTES, :1300 find_free_slots; Bulwark components/calendar/find-a-time.tsx, participant-availability.tsx (findFreeSlots, availabilityFor); B-CAL-049/050.
// Depends on Principal/getAvailability (G-009).

namespace Barbican.Core.Calendar.Editing;

/// <summary>Free/busy rules for the guests' availability and the "Find a time" suggestions.</summary>
public static class FindATime
{
    public const int SearchWorkingDays = 10;
    public const int MaxSuggestions = 5;
    public const int GridMinutes = 30;

    /// <summary>The status of one guest for [from, to) (UTC ms) from their busy periods (start, end, tentative): busy wins over tentative.</summary>
    public static AvailabilityStatus AvailabilityFor(IReadOnlyList<(long Start, long End, bool Tentative)> periods, long from, long to)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var status = new AvailabilityStatus("Free", 1);
        foreach (var (start, end, tentative) in periods)
        {
            if (start < to && end > from)
            {
                if (!tentative)
                {
                    return new AvailabilityStatus("Busy", 3);
                }

                status = new AvailabilityStatus("Tentative", 2);
            }
        }

        return status;
    }

    /// <summary>Up to <paramref name="max"/> slots of <paramref name="durationMs"/> within the working hours of the next working days (<paramref name="workingDays"/> as DayOfWeek numbers, empty means Monday to Friday; hours <paramref name="startHour"/> to <paramref name="endHour"/>,
    /// 8 to 20 when not a valid range), on a 30-minute grid, not before <paramref name="now"/>, overlapping no busy period (UTC ms; slots are on the user's clock).</summary>
    public static IReadOnlyList<FreeSlot> FindFreeSlots(DateTime now, long durationMs, IReadOnlyList<int> workingDays, int startHour, int endHour, IReadOnlyList<(long Start, long End)> busy, int max)
    {
        ArgumentNullException.ThrowIfNull(workingDays);
        ArgumentNullException.ThrowIfNull(busy);
        var days = workingDays.Count == 0 ? [1, 2, 3, 4, 5] : workingDays;
        var (sh, eh) = endHour > startHour ? (startHour, endHour) : (8, 20);
        var slots = new List<FreeSlot>();
        if (durationMs <= 0 || max <= 0)
        {
            return slots;
        }

        var day = now.Date;
        var searched = 0;
        for (var i = 0; i < 366 && searched < SearchWorkingDays; i++, day = day.AddDays(1))
        {
            if (!days.Contains((int)day.DayOfWeek))
            {
                continue;
            }

            searched++;
            var dayEnd = day.AddHours(eh);
            for (var m = sh * 60; m < eh * 60;)
            {
                var s = day.AddMinutes(m);
                var e = s.AddMilliseconds(durationMs);
                m += GridMinutes;
                if (e > dayEnd)
                {
                    break;
                }

                if (s < now)
                {
                    continue;
                }

                long bs = UtcMs(s), be = UtcMs(e);
                if (busy.Any(b => b.Start < be && b.End > bs))
                {
                    continue;
                }

                slots.Add(new FreeSlot(s, e));
                if (slots.Count >= max)
                {
                    return slots;
                }
            }
        }

        return slots;
    }

    private static long UtcMs(DateTime wall) => new DateTimeOffset(wall, TimeZoneInfo.Local.GetUtcOffset(wall)).ToUnixTimeMilliseconds();
}
