// Mirrors: Rust schedule.rs:26 presets; Bulwark lib/schedule-presets.ts. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose.Schedule;

/// <summary>A one-click schedule time: its label ("This morning") and the instant.</summary>
public sealed record SchedulePreset(string Label, DateTimeOffset At);
