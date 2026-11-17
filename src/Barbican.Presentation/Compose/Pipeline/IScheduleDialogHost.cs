// Mirrors: Rust schedule.rs:104 open_dialog (the dialog the Scheduled view Reschedule opens); I-241. Owner send-pipeline (the interface), composer-ui (the view that implements it).

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>Shows the schedule dialog for a view model and returns the instant the user chose, or null when it was dismissed.</summary>
public interface IScheduleDialogHost
{
    Task<DateTimeOffset?> ShowAsync(ScheduleDialogViewModel dialog, CancellationToken ct);
}
