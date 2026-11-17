// Mirrors: Rust schedule.rs:104 open_dialog; the IScheduleDialogHost contract of send-pipeline (the Scheduled banner Reschedule). Implemented by composer-ui over the dialog host.

using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Compose;

/// <summary>Shows the schedule dialog in the dialog host and returns the time the user chose.</summary>
public sealed class ScheduleDialogHost(IDialogService dialogs) : IScheduleDialogHost
{
    public async Task<DateTimeOffset?> ShowAsync(ScheduleDialogViewModel dialog, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        // the dialog host's own OK or Cancel ends the dialog without a time: that is a cancel
        _ = dialogs.ShowAsync(new ScheduleDialogRequest(dialog), ct).ContinueWith(_ => dialog.Cancel(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
#pragma warning disable VSTHRD003 // the dialog view model owns this completion source
        return await dialog.Completion.ConfigureAwait(true);
#pragma warning restore VSTHRD003
    }
}
