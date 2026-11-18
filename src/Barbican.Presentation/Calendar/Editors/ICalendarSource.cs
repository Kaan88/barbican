// Mirrors: Rust calendar.rs calendars()/with_raw()/raw_event() (what the editors read: the calendars, the events, the user's addresses) and cal_editor.rs:487 writable, calendar.rs:796 default_task_list.
// One read seam over ICalendarStore for the active login so the editor, task and menu view models are tested with a fake.

using System.Text.Json.Nodes;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>What the calendar editors read of the active login.</summary>
public interface ICalendarSource
{
    /// <summary>The active login (null when none).</summary>
    string? AccountKey { get; }

    /// <summary>The user's own addresses, lower case.</summary>
    IReadOnlyList<string> Mine { get; }

    IReadOnlyList<JsonNode> Calendars { get; }

    IReadOnlyList<JsonNode> Events { get; }

    /// <summary>Calendars that hold a webcal feed (read only for the user).</summary>
    IReadOnlySet<string> Subscriptions { get; }

    /// <summary>Raised when the store changed (any thread).</summary>
    event EventHandler? Changed;
}
