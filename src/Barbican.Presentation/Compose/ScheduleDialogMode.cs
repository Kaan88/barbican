// Mirrors: Bulwark components/email/schedule-send-dialog.tsx (a composer's "Schedule send" and the Scheduled view's "Reschedule"); I-241. Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>What the schedule dialog is for.</summary>
public enum ScheduleDialogMode
{
    /// <summary>Pick a time for the message in the open composer.</summary>
    Compose,

    /// <summary>Pick a new time for a message already scheduled.</summary>
    Reschedule,
}
