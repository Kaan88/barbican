// Mirrors: Rust cal_editor.rs:1247 availability_for, :1294-1296 SEARCH_WORKING_DAYS, MAX_SUGGESTIONS, GRID_MINUTES, :1300 find_free_slots; Bulwark components/calendar/find-a-time.tsx, participant-availability.tsx (findFreeSlots, availabilityFor); B-CAL-049/050.
// Depends on Principal/getAvailability (G-009). Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Free/busy rules for the guests' availability and the "Find a time" suggestions.</summary>
public static class FindATime
{
    public const int SearchWorkingDays = 10;

    public const int MaxSuggestions = 5;

    public const int GridMinutes = 30;

    /// <summary>The status of one guest for [from, to) (UTC ms) from their busy periods (start, end, tentative): busy wins over tentative.</summary>
    public static AvailabilityStatus AvailabilityFor(IReadOnlyList<(long Start, long End, bool Tentative)> periods, long from, long to) => throw new PortPendingException("FindATime.AvailabilityFor");

    /// <summary>Up to <paramref name="max"/> slots of <paramref name="durationMs"/> within the working hours of the next working days (<paramref name="workingDays"/> as DayOfWeek numbers, empty means Monday to Friday; hours <paramref name="startHour"/> to <paramref name="endHour"/>,
    /// 8 to 20 when not a valid range), on a 30-minute grid, not before <paramref name="now"/>, overlapping no busy period (UTC ms; slots are on the user's clock).</summary>
    public static IReadOnlyList<FreeSlot> FindFreeSlots(DateTime now, long durationMs, IReadOnlyList<int> workingDays, int startHour, int endHour, IReadOnlyList<(long Start, long End)> busy, int max) => throw new PortPendingException("FindATime.FindFreeSlots");
}
