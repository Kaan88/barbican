// Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>
/// One event save: a create (<c>EventId</c> null; <c>Data</c> the full object, with <c>calendarIds</c>) or an update of an event or one of its occurrences. <c>AccountKey</c> is the login (IAccountManager key; the event id may carry a shared
/// account's namespace); <c>Data</c> comes from <see cref="EventEditor.BuildData"/>; <c>RecurrenceId</c> is the occurrence being edited, for <see cref="EditScope.Occurrence"/> and <see cref="EditScope.Following"/>.
/// </summary>
public sealed record EventWrite(string AccountKey, string? EventId, JsonObject Data, EditScope Scope, string? RecurrenceId, bool SendSchedulingMessages);
