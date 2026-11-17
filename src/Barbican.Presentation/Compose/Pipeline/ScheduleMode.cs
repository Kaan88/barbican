// Mirrors: Rust schedule.rs:104 Mode (Compose, Reschedule). Owner send-pipeline.

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>What the schedule dialog is for.</summary>
public enum ScheduleMode
{
    /// <summary>Schedule the message in the composer.</summary>
    Compose,

    /// <summary>Move the release time of a scheduled message (the Scheduled view's Reschedule).</summary>
    Reschedule,
}
