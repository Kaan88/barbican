// Mirrors: Bulwark lib/plugin-sandbox/day-labels.ts:5-22 DayLabel and RegisteredDayLabel (a date, a short text, a title, a dot).
namespace Barbican.Core.Plugins.Calendar;

/// <summary>One label an add-on put on a calendar day (<c>api.ui.setDayLabels</c>).</summary>
/// <param name="Date">The day.</param>
/// <param name="Text">Shown under the day's name in the day and week views, at most 24 characters, trimmed; null when none.</param>
/// <param name="Title">The hover text, at most 120 characters, trimmed; null when none.</param>
/// <param name="Dot">A dot on the day in the small month calendar.</param>
public sealed record PluginDayLabel(DateOnly Date, string? Text, string? Title, bool Dot);
