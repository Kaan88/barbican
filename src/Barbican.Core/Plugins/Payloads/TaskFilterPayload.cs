// Mirrors: Bulwark lib/plugin-hook-catalog.ts onTaskFilterChange payload { filter, showCompleted }. Owner cal-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The Tasks view filter changed: the filter name (all, pending, overdue, completed) and whether completed tasks are shown.</summary>
public sealed record TaskFilterPayload(string Filter, bool ShowCompleted);
