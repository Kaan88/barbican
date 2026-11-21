// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:132-137 (Timing).
namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>JSCalendar timing worked out from a plugin's start / end / duration / timeZone / isAllDay.</summary>
internal sealed record PluginTiming(string Start, string Duration, string? TimeZone, bool ShowWithoutTime);
