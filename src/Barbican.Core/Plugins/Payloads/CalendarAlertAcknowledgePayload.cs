// Mirrors: Bulwark onCalendarAlertAcknowledge { alertId, eventId } or { alertId, taskId } (lib/plugin-hook-catalog.ts:187). Owner cal-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The user dealt with a reminder (dismissed it).</summary>
public sealed record CalendarAlertAcknowledgePayload(string AlertId, string? EventId, string? TaskId);
