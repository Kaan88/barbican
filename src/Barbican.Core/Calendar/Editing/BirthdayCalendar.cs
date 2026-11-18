// Mirrors: Rust cal_extra.rs:84 birthday_parts, :99 birthdays_in, :125 birthday_occurrences, :128 birthday; Bulwark lib/birthday-calendar.ts; B-CAL-003/046 (fixed calendar id __birthday-calendar__, all-day events "cake Name (age)", ids bday:card:entry:year, 29 February on 28 February in common years).

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Contact birthdays as agenda items.</summary>
public static class BirthdayCalendar
{
    public const string CalendarId = "__birthday-calendar__";

    /// <summary>(month, day, year or null) of a birth date: a PartialDate object, "2004-02-29", "--12-24" (no year) or a Timestamp.</summary>
    public static (int Month, int Day, int? Year)? PartsOf(JsonNode date)
    {
        ArgumentNullException.ThrowIfNull(date);
        if (date is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (s.StartsWith("--", StringComparison.Ordinal))
            {
                var rest = s[2..].Split('-', 2);
                return rest.Length == 2 && rest[1].Length >= 2 && int.TryParse(rest[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                    && int.TryParse(rest[1][..2], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? (m, d, null) : null;
            }

            return s.Length >= 10 && DateTime.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? (t.Month, t.Day, t.Year) : null;
        }

        if (string.Equals((string?)date["@type"], "Timestamp", StringComparison.Ordinal))
        {
            return date["utc"] is { } utc ? PartsOf(utc) : null;
        }

        return Int(date["month"]) is { } mm && Int(date["day"]) is { } dd ? (mm, dd, Int(date["year"])) : null;
    }

    private static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    /// <summary>The birthdays of the cards in [from, to), as all-day occurrences of kind Birthday: title with the age when the year is known ("cake Ann Lee (36)"), id "bday:c1:b:2026", calendar <see cref="CalendarId"/>.</summary>
    public static IReadOnlyList<CalendarOccurrence> In(IReadOnlyList<JsonNode> cards, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var output = new List<CalendarOccurrence>();
        foreach (var card in cards)
        {
            if (card["anniversaries"] is not JsonObject anniversaries)
            {
                continue;
            }

            var name = CalendarPeople.CardName(card);
            if (name.Length == 0)
            {
                continue;
            }

            foreach (var (key, a) in anniversaries)
            {
                if (a is null || !string.Equals((string?)a["kind"], "birth", StringComparison.Ordinal) || a["date"] is not { } raw || PartsOf(raw) is not var (month, day, year))
                {
                    continue;
                }

                for (var y = from.Year; y <= to.Year; y++)
                {
                    DateOnly date;
                    if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(y, month))
                    {
                        date = new DateOnly(y, month, day);
                    }
                    else if (month == 2 && day == 29)
                    {
                        date = new DateOnly(y, 2, 28);
                    }
                    else
                    {
                        continue;
                    }

                    if (date < from || date >= to)
                    {
                        continue;
                    }

                    var age = year is { } born && y - born > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({y - born})") : "";
                    var start = date.ToDateTime(TimeOnly.MinValue);
                    output.Add(new CalendarOccurrence(
                        string.Create(CultureInfo.InvariantCulture, $"bday:{(string?)card["id"]}:{key}:{y}"), "", null, $"\U0001F382 {name}{age}", start, start.AddDays(1), true, CalendarId, "", CalendarOccurrenceKind.Birthday, false));
                }
            }
        }

        return output;
    }
}
