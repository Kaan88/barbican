// Mirrors: Bulwark onBeforeCalendarAlert and onCalendarAlert CalendarAlertView ({ id, eventId, eventTitle, triggerTime } or { id, taskId, taskTitle, triggerTime }; lib/plugin-hook-catalog.ts:185-186). Owner cal-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A reminder about to be shown or just fired: for an event <c>EventId</c> and <c>EventTitle</c> are set, for a task <c>TaskId</c> and <c>TaskTitle</c>. <c>TriggerTime</c> is the fire time in UTC milliseconds.</summary>
public sealed record CalendarAlertPayload(string Id, string? EventId, string? EventTitle, string? TaskId, string? TaskTitle, long TriggerTime);
