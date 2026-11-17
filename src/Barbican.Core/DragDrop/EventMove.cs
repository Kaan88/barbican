// Mirrors: Rust dnd.rs:176-222 moved_event, iso_minutes, update_args (calendar event drags, M4 consumes them); RFC 8984 (durations);
// JMAP Calendars sendSchedulingMessages. Pure functions; the calendar UI is M4.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.DragDrop;

/// <summary>The grid-snapping arithmetic of dragging a calendar event.</summary>
public static class EventMove
{
    /// <summary>Minutes per grid step (Rust STEP_MIN).</summary>
    public const int StepMinutes = 15;

    /// <summary>New start and duration of an event dragged by days and minutes, or stretched at its end (never under one step).</summary>
    public static (DateTime Start, TimeSpan Duration) Moved(DateTime start, TimeSpan duration, int days, int minutes, bool resize)
    {
        if (resize)
        {
            var d = Math.Max((int)duration.TotalMinutes + Snap(minutes), StepMinutes);
            return (start, TimeSpan.FromMinutes(d));
        }

        return (start.AddDays(days).AddMinutes(Snap(minutes)), duration);
    }

    /// <summary>An ISO 8601 duration for a whole number of minutes (PT1H30M).</summary>
    public static string IsoMinutes(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return (h, m) switch
        {
            (0, _) => string.Create(CultureInfo.InvariantCulture, $"PT{m}M"),
            (_, 0) => string.Create(CultureInfo.InvariantCulture, $"PT{h}H"),
            _ => string.Create(CultureInfo.InvariantCulture, $"PT{h}H{m}M"),
        };
    }

    /// <summary>CalendarEvent/set arguments for a moved event: with attendees, sendSchedulingMessages is true.</summary>
    public static JsonObject UpdateArgs(string id, JsonNode patch, JsonNode eventJson)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var attendees = eventJson?["participants"] is JsonObject p && p.Count > 0;
        var args = new JsonObject { ["update"] = new JsonObject { [id] = patch } };
        if (attendees)
        {
            args["sendSchedulingMessages"] = true;
        }

        return args;
    }

    // Rust rounds half away from zero (f64::round).
    private static int Snap(int minutes) =>
        (int)Math.Round(minutes / (double)StepMinutes, MidpointRounding.AwayFromZero) * StepMinutes;
}
