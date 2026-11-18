// Mirrors: Rust cal_editor.rs:210 Form. Contract from the M4 plan section 4.3; owner cal-edit. The event editor view model (cal-ui) fills one and calls <see cref="EventEditor.BuildData"/>.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;

namespace Barbican.Core.Calendar.Editing;

/// <summary>What the event editor holds, as plain values (for building the save). Times are on the form's clock (<see cref="TimeZone"/>); for an all-day event <c>End</c> is the last day.</summary>
public sealed record EventForm
{
    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string Location { get; init; } = "";

    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public bool AllDay { get; init; }

    /// <summary>IANA zone name; null for floating.</summary>
    public string? TimeZone { get; init; }

    public string Calendar { get; init; } = "";

    /// <summary>0 none, 1 daily, 2 weekly, 3 monthly, 4 yearly, 5 custom (<see cref="Custom"/>).</summary>
    public int Repeat { get; init; }

    public RecurrenceRule? Custom { get; init; }

    public bool UseDefaultAlerts { get; init; } = true;

    public JsonNode? DefaultAlerts { get; init; }

    public IReadOnlyList<AlertEditRow> Rows { get; init; } = [];

    public JsonObject Kept { get; init; } = [];

    public IReadOnlyList<string> ReservedAlertIds { get; init; } = [];

    public IReadOnlyList<CalendarPerson> Attendees { get; init; } = [];

    /// <summary>Send scheduling messages (the user chose to notify).</summary>
    public bool Send { get; init; }

    /// <summary>The user organises the event.</summary>
    public bool Organizer { get; init; } = true;

    /// <summary>0 busy, 1 free.</summary>
    public int FreeBusy { get; init; }

    /// <summary>0 public, 1 private, 2 secret; null leaves it as it is.</summary>
    public int? Privacy { get; init; }

    /// <summary>The user's own addresses, lower case.</summary>
    public IReadOnlyList<string> Me { get; init; } = [];
}
