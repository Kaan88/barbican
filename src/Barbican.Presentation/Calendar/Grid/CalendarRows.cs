// Mirrors: Rust calendar.rs:63 PALETTE, :93 color_for (the 8-colour palette in order, the calendar's own colour when it has one), :362 first_calendar; Bulwark components/calendar/calendar-sidebar-panel.tsx; B-CAL-002, 058.

using System.Text.Json.Nodes;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The sidebar rows from the calendars' JSCalendar objects.</summary>
public static class CalendarRows
{
    /// <summary>The fixed id of the contact birthday calendar (B-CAL-003).</summary>
    public const string BirthdayId = "__birthday-calendar__";

    private static readonly string[] Palette = ["#3b82f6", "#22c55e", "#f97316", "#a855f7", "#ec4899", "#14b8a6", "#eab308", "#ef4444"];

    /// <summary>The rows in the order of the objects; a calendar without a colour takes the palette's next one by its position (B-CAL-002); the name has no account suffix (B-CAL-058).</summary>
    public static IReadOnlyList<CalendarListRow> From(IReadOnlyList<JsonNode> calendars, string accountKey)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        var rows = new List<CalendarListRow>(calendars.Count);
        for (var i = 0; i < calendars.Count; i++)
        {
            var c = calendars[i];
            var id = Str(c, "id");
            var color = Hex(Str(c, "_color")) ?? Hex(Str(c, "color")) ?? Palette[i % Palette.Length];
            var rights = c["myRights"] as JsonObject;
            var canEdit = rights is null || Bool(rights, "mayWriteAll") || Bool(rights, "mayWriteOwn");
            rows.Add(new CalendarListRow(
                id,
                accountKey,
                Str(c, "name") is { Length: > 0 } name ? name : "Calendar",
                color,
                true,
                Bool(c, "isDefault"),
                id.Contains('~', StringComparison.Ordinal),
                Bool(c, "isSubscribed"),
                canEdit));
        }

        return rows;
    }

    /// <summary>The calendar an event belongs to: the first of its calendarIds set to true, else the first key.</summary>
    public static string FirstCalendar(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        if (ev["calendarIds"] is not JsonObject ids)
        {
            return string.Empty;
        }

        foreach (var pair in ids)
        {
            if (pair.Value is JsonValue v && v.TryGetValue<bool>(out var b) && b)
            {
                return pair.Key;
            }
        }

        return ids.FirstOrDefault().Key ?? string.Empty;
    }

    /// <summary>"#rrggbb" from "#rgb", "#rrggbb" or "rrggbb"; null when it is none of them.</summary>
    public static string? Hex(string? text)
    {
        var s = (text ?? string.Empty).Trim().TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s.Select(c => new string(c, 2)));
        }

        return s.Length == 6 && s.All(Uri.IsHexDigit) ? "#" + s.ToLowerInvariant() : null;
    }

    private static string Str(JsonNode node, string key) => node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static bool Bool(JsonNode node, string key) => node[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
