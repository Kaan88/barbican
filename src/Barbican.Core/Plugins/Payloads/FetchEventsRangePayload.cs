// Mirrors: Bulwark onBeforeFetchEvents { start, end } (lib/plugin-hook-catalog.ts:171). Owner cal-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The range the calendar is about to load, as LocalDateTime text ("2026-11-01T00:00:00"); null for a side the load does not bound (the desktop loads every event). A plugin may return a narrower range.</summary>
public sealed record FetchEventsRangePayload(string? Start, string? End);
