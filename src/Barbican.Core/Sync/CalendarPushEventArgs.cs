// Mirrors: PORT-PLAN section 3.4 (push types Calendar, CalendarEvent, CalendarEventNotification), M4 plan D-M4-12; Rust main.rs:486 PUSH_TYPES (calendars were never pushed there; G-044). Owner cal-data.

using Barbican.Core.Jmap;

namespace Barbican.Core.Sync;

/// <summary>A calendar state change of one account.</summary>
public sealed class CalendarPushEventArgs(IJmapClient client, IReadOnlyCollection<string> types) : EventArgs
{
    public IJmapClient Client { get; } = client;

    /// <summary>Some of "Calendar", "CalendarEvent", "CalendarEventNotification".</summary>
    public IReadOnlyCollection<string> Types { get; } = types;
}
