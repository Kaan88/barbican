// The fake server's calendar writes (JMAP Calendars draft; Stalwart crates/jmap/src/calendar/set.rs, calendar_event/set.rs): Calendar/set (create, update, destroy, onSuccessSetIsDefault, onDestroyRemoveEvents) and CalendarEvent/set (create, update with JSON-pointer
// patches through FakeCalendars.ApplyPatch, destroy, sendSchedulingMessages recorded in FakeCalendars.SchedulingSent, notFound and invalidProperties errors). Answers unknownMethod until stream cal-edit fills it (M4 plan section 4.7). Owner cal-edit.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) CalendarSet(string type, JsonObject args)
    {
        _ = type;
        _ = args;
        return Error("unknownMethod");
    }
}
