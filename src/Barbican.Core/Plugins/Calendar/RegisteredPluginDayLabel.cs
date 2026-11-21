// Mirrors: Bulwark lib/plugin-sandbox/day-labels.ts:16-19 RegisteredDayLabel (the label with its add-on's id and name).
namespace Barbican.Core.Plugins.Calendar;

/// <summary>A day label with the add-on that set it.</summary>
public sealed record RegisteredPluginDayLabel(string PackageId, string PackageName, PluginDayLabel Label);
