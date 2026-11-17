// Mirrors: Rust schedule.rs:240-292 (action: reschedule opens the dialog with the message own time, edit = cancel_submission then open the draft, cancel = cancel_submission with the toast "Sending cancelled. The message is in Drafts."; reschedule toasts
// "Rescheduling..." then "Rescheduled for {label}"); Bulwark components/email/message-actions.ts (Moved to Drafts with Open). I-241, I-282. Owner send-pipeline.

using Barbican.Core.Compose;
using Barbican.Core.Compose.Schedule;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>What the scheduled banner buttons and the Scheduled folder row menu do.</summary>
public sealed class ScheduledBannerActions(IScheduledSends scheduled, IActiveMail active, IScheduleDialogHost dialogs, IComposerService composer, IToastService toasts, IPrefsStore prefs, TimeProvider time)
{
    private bool TwelveHour => string.Equals(prefs.Current.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal);

    /// <summary>Runs <paramref name="actionId"/> (a <see cref="ScheduledBannerSource"/> action) for the message <paramref name="emailId"/>; the text is the result line (the toast says the same).</summary>
    public async Task<string> RunAsync(string emailId, string actionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailId);
        ArgumentNullException.ThrowIfNull(actionId);
        if (active.AccountKey is not { } account)
        {
            return Say("This message is no longer scheduled.", true);
        }

        var list = await scheduled.ListAsync(account, ct).ConfigureAwait(false);
        if (!list.IsOk || list.Value!.FirstOrDefault(m => string.Equals(m.EmailId, emailId, StringComparison.Ordinal)) is not { } message)
        {
            return Say("This message is no longer scheduled.", true);
        }

        return actionId switch
        {
            ScheduledBannerSource.Reschedule => await RescheduleAsync(account, message, ct).ConfigureAwait(false),
            ScheduledBannerSource.CancelAndEdit => await CancelAsync(account, message, true, ct).ConfigureAwait(false),
            ScheduledBannerSource.CancelSend => await CancelAsync(account, message, false, ct).ConfigureAwait(false),
            _ => Say("Unknown action.", true),
        };
    }

    private async Task<string> RescheduleAsync(string account, ScheduledMessage message, CancellationToken ct)
    {
        var dialog = new ScheduleDialogViewModel(ScheduleMode.Reschedule, time, scheduled.MaxDelayedSend(account), TwelveHour, message.At);
        var chosen = await dialogs.ShowAsync(dialog, ct).ConfigureAwait(false);
        if (chosen is not { } at)
        {
            return "Cancelled.";
        }

        toasts.Show(new ToastRequest("Rescheduling..."));
        var r = await scheduled.RescheduleAsync(account, message.SubmissionId, at, ct).ConfigureAwait(false);
        return r.IsOk
            ? Say($"Rescheduled for {ScheduleRules.Label(at, TwelveHour, time.GetUtcNow(), time.LocalTimeZone)}.", false)
            : Say(r.Error!.Message, true);
    }

    private async Task<string> CancelAsync(string account, ScheduledMessage message, bool edit, CancellationToken ct)
    {
        var r = await scheduled.CancelAsync(account, message.SubmissionId, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Say($"Couldn't cancel the scheduled send: {r.Error!.Message}", true);
        }

        var request = new ComposeRequest(account, ComposeKind.EditDraft) { SourceEmailId = r.Value };
        if (edit)
        {
            await composer.OpenAsync(request, ct).ConfigureAwait(false);
            return "Sending cancelled.";
        }

        toasts.Show(new ToastRequest("Sending cancelled. Moved to Drafts.", false, "Open", token => composer.OpenAsync(request, token)));
        return "Sending cancelled. Moved to Drafts.";
    }

    private string Say(string text, bool error)
    {
        toasts.Show(new ToastRequest(text, error));
        return text;
    }
}
