// Mirrors: Bulwark lib/plugin-types.ts:575-613 CalendarEventInput (start, end, duration, timeZone, isAllDay) as the plugin wrote them; absent is null.
namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed record PluginTimingInput(string? Start, string? End, string? Duration, string? TimeZone, bool? IsAllDay);
