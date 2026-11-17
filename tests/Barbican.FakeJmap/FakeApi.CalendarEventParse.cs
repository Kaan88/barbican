// Mirrors: JMAP Calendars CalendarEvent/parse; Stalwart crates/jmap/src/calendar_event/parse.rs. Stage F of M2 wires the dispatch arm in FakeApi.cs; this file is the stub the owner (reader) fills (M2 plan 3.1). Until then the method answers unknownMethod,
// exactly what the M1 fake did for it.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) CalendarEventParse(JsonObject args) => Error("unknownMethod");
}
