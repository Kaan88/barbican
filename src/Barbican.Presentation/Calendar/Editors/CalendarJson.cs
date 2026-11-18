using System.Globalization;
using System.Text.Json.Nodes;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>Reading the JSCalendar objects the calendar views hold.</summary>
public static class CalendarJson
{
    /// <summary>The calendar colours the sidebar and the menus offer (server_settings::CAL_COLORS).</summary>
    public static IReadOnlyList<(string Hex, string Name)> Colors { get; } =
    [
        ("#3b82f6", "Blue"), ("#ef4444", "Red"), ("#22c55e", "Green"), ("#f59e0b", "Amber"), ("#8b5cf6", "Violet"), ("#ec4899", "Pink"), ("#14b8a6", "Teal"),
        ("#f97316", "Orange"), ("#06b6d4", "Cyan"), ("#84cc16", "Lime"), ("#6366f1", "Indigo"), ("#a855f7", "Purple"), ("#e11d48", "Rose"), ("#0ea5e9", "Sky"),
    ];

    public static string Str(JsonNode? node, string key) => node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    public static bool Flag(JsonNode? node, string key) => node?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public static string Id(JsonNode node) => Str(node, "id");

    /// <summary>The event id without an occurrence suffix ("e|2026-01-01T00:00:00" is "e"; "task:ID" is "ID").</summary>
    public static string BaseId(string occurrenceId)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        var id = occurrenceId.StartsWith("task:", StringComparison.Ordinal) ? occurrenceId["task:".Length..] : occurrenceId;
        var bar = id.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? id : id[..bar];
    }

    public static string? RecurrenceIdOf(string occurrenceId)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        var bar = occurrenceId.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? null : occurrenceId[(bar + 1)..];
    }

    public static bool IsTask(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var type = Str(ev, "@type");
        return string.Equals(type, "task", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(type, "Event", StringComparison.Ordinal) && (ev["progress"] is JsonValue || ev["due"] is not null || ev["percentComplete"] is not null));
    }

    /// <summary>The first calendar an event or task is in.</summary>
    public static string FirstCalendar(JsonNode ev) => ev["calendarIds"] is JsonObject m ? m.Select(p => p.Key).FirstOrDefault() ?? string.Empty : string.Empty;

    /// <summary>A calendar of another account (ids are namespaced "x~id").</summary>
    public static bool IsShared(string calendarId) => calendarId.Contains('~', StringComparison.Ordinal);

    public static bool Writable(JsonNode calendar, ISet<string>? subscriptions = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (subscriptions?.Contains(Id(calendar)) == true)
        {
            return false;
        }

        return calendar["myRights"] is not JsonObject r || Flag(r, "mayWriteAll") || Flag(r, "mayWriteOwn") || Flag(r, "mayAddItems");
    }

    public static DateTime? ParseLocal(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 10)
        {
            return null;
        }

        var head = text.Length >= 19 ? text[..19] : text.PadRight(10)[..10];
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"];
        return DateTime.TryParseExact(head, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>The machine's zone as an IANA name (Windows ids are converted), "UTC" when unknown.</summary>
    public static string LocalZoneId()
    {
        var id = TimeZoneInfo.Local.Id;
        if (id.Contains('/', StringComparison.Ordinal) || string.Equals(id, "UTC", StringComparison.Ordinal))
        {
            return id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : "UTC";
    }

    /// <summary>A length as an ISO 8601 duration: "PT1H30M", "P1D" for whole days.</summary>
    public static string FormatDuration(TimeSpan length)
    {
        var secs = (long)Math.Max(0, length.TotalSeconds);
        if (secs > 0 && secs % 86400 == 0)
        {
            return "P" + (secs / 86400).ToString(CultureInfo.InvariantCulture) + "D";
        }

        var h = secs / 3600;
        var m = secs % 3600 / 60;
        var sec = secs % 60;
        return "PT" + (h > 0 ? h.ToString(CultureInfo.InvariantCulture) + "H" : string.Empty) + (m > 0 ? m.ToString(CultureInfo.InvariantCulture) + "M" : string.Empty) + (sec > 0 || secs == 0 ? sec.ToString(CultureInfo.InvariantCulture) + "S" : string.Empty);
    }

    public static string FormatLocal(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>An ISO 8601 duration (P1DT2H30M, PT45M, P1W) as a time span; zero when unreadable.</summary>
    public static TimeSpan ParseDuration(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != 'P')
        {
            return TimeSpan.Zero;
        }

        double total = 0;
        var inTime = false;
        var number = new System.Text.StringBuilder();
        foreach (var c in text.AsSpan(1))
        {
            if (char.IsDigit(c) || c == '.')
            {
                number.Append(c);
                continue;
            }

            if (c == 'T')
            {
                inTime = true;
                continue;
            }

            if (!double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                return TimeSpan.Zero;
            }

            number.Clear();
            total += c switch
            {
                'W' => n * 7 * 86400,
                'D' => n * 86400,
                'H' when inTime => n * 3600,
                'M' when inTime => n * 60,
                'S' when inTime => n,
                _ => 0,
            };
        }

        return TimeSpan.FromSeconds(total);
    }
}
