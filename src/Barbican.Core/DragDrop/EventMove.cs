// Mirrors: Rust dnd.rs:176-222 moved_event, iso_minutes, update_args (calendar event drags, M4 consumes them); RFC 8984 (durations);
// JMAP Calendars sendSchedulingMessages. Stub from stage F of M2; owner actions (pure functions; the calendar UI is M4).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.DragDrop;

/// <summary>The grid-snapping arithmetic of dragging a calendar event.</summary>
public static class EventMove
{
    /// <summary>Minutes per grid step (Rust STEP_MIN).</summary>
    public const int StepMinutes = 15;

    /// <summary>New start and duration of an event dragged by days and minutes, or stretched at its end (never under one step).</summary>
    public static (DateTime Start, TimeSpan Duration) Moved(DateTime start, TimeSpan duration, int days, int minutes, bool resize)
    {
        throw new PortPendingException("dnd.rs:176 moved_event");
    }

    /// <summary>An ISO 8601 duration for a whole number of minutes (PT1H30M).</summary>
    public static string IsoMinutes(int minutes)
    {
        throw new PortPendingException("dnd.rs:186 iso_minutes");
    }

    /// <summary>CalendarEvent/set arguments for a moved event: with attendees, sendSchedulingMessages is true.</summary>
    public static JsonObject UpdateArgs(string id, JsonNode patch, JsonNode eventJson)
    {
        throw new PortPendingException("dnd.rs:194 update_args");
    }
}
