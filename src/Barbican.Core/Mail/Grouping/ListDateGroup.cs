// Mirrors: Bulwark lib/list-date-groups.ts (ListDateGroup, listDateGroup, listDateGroupLabel). Owner mailcore.

namespace Barbican.Core.Mail.Grouping;

/// <summary>The date groups of the message list: Today, Yesterday, the weekday for the rest of the last seven days, Last week, Last month, Older.</summary>
/// <param name="Kind">today, yesterday, weekday, last_week, last_month or older.</param>
/// <param name="Weekday">For <c>weekday</c> only: 0 for Sunday as in JavaScript's <c>Date.getDay()</c>.</param>
public readonly record struct ListDateGroup(string Kind, int Weekday = 0)
{
    public static ListDateGroup Today { get; } = new("today");

    public static ListDateGroup Yesterday { get; } = new("yesterday");

    public static ListDateGroup LastWeek { get; } = new("last_week");

    public static ListDateGroup LastMonth { get; } = new("last_month");

    public static ListDateGroup Older { get; } = new("older");

    public static ListDateGroup OnWeekday(DayOfWeek day) => new("weekday", (int)day);

    /// <summary>The header text (Bulwark en locale <c>date_groups.*</c>; a weekday is its English name).</summary>
    public string Label => Kind switch
    {
        "today" => "Today",
        "yesterday" => "Yesterday",
        "weekday" => ((DayOfWeek)Weekday).ToString(),
        "last_week" => "Last week",
        "last_month" => "Last month",
        _ => "Older",
    };
}
