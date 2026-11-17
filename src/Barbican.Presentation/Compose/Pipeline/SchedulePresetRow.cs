// Mirrors: Rust schedule.rs:117-123 open_dialog (SPreset: label and "Tue 17 Nov, 08:00"). Owner send-pipeline.

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>A one-click time of the schedule dialog: its label, the date and time it stands for, and the instant.</summary>
public sealed record SchedulePresetRow(string Label, string When, DateTimeOffset At);
