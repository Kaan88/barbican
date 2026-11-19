// Mirrors: Bulwark components/email/email-composer.tsx handleSend (the order: images still uploading, invalid addresses, no recipient, the warnings, the external-recipient confirm, then submit; sendConfirmation),
// send-warnings-dialog.tsx, external-recipient-confirm.tsx, schedule-send-dialog.tsx; M3 plan section 9 item 5 (one dialog lists every warning, external recipients included: an improvement over Rust's "press Send again");
// I-239, I-240, I-241, I-259, I-260. The send flow of ComposerViewModel.

using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose.Pipeline;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    private bool _sending;
    private bool _sendFlowActive;
    private ScheduleDialogViewModel? _schedule;

    /// <summary>A send is in flight (the buttons are off and the composer cannot be closed).</summary>
    public bool IsSending
    {
        get => _sending;
        private set
        {
            if (SetProperty(ref _sending, value)) OnPropertyChanged(nameof(CanSend));
        }
    }

    /// <summary>Whether the send controls are available; malformed To entries remain actionable so validation can explain them.</summary>
    public bool CanSend => !_closed && !IsSending && !_sendFlowActive && Schedule is null
        && RecipientText.Expand([.. To.Recipients, .. string.IsNullOrWhiteSpace(To.Input) ? [] : new[] { RecipientText.ParseRecipient(To.Input) }]).Count > 0;

    /// <summary>The schedule dialog, or null (the view draws it over the composer).</summary>
    public ScheduleDialogViewModel? Schedule
    {
        get => _schedule;
        private set
        {
            if (SetProperty(ref _schedule, value)) OnPropertyChanged(nameof(CanSend));
        }
    }

    /// <summary>Send, Ctrl+Enter. False when the checks stopped it or the send failed; true when the message went (the composer then closes).</summary>
    public Task<bool> SendAsync() => SendAtAsync(null);

    /// <summary>Opens the schedule dialog (Ctrl+Shift+Enter, the send arrow's "Schedule send"); the chosen time goes through the same checks as a send.</summary>
    public async Task<bool> OpenScheduleAsync()
    {
        if (_closed || IsSending || _sendFlowActive || Schedule is not null)
        {
            return false;
        }

        var state = new ScheduleDialogViewModel(ScheduleMode.Compose, _c.Time, _c.Sender.MaxDelayedSeconds(AccountKey), twelveHour: false, zone: (_c.DisplayZone?.Invoke() ?? _c.Time.LocalTimeZone));
        Schedule = state;
        var when = await state.Completion.ConfigureAwait(true);
        Schedule = null;
        return when is { } at && await SendAtAsync(at).ConfigureAwait(true);
    }

    /// <summary>The view calls this when the schedule dialog is dismissed (Escape).</summary>
    public void CancelSchedule() => Schedule?.Cancel();

    private async Task<bool> SendAtAsync(DateTimeOffset? scheduledAt)
    {
        if (_closed || IsSending || _sendFlowActive || Schedule is not null)
        {
            return false;
        }

        _sendFlowActive = true;
        OnPropertyChanged(nameof(CanSend));
        try
        {
            CommitInputs();
            if (await StopBeforeSendAsync().ConfigureAwait(true))
            {
                return false;
            }

            await WaitForSaveAsync().ConfigureAwait(true);
            var draft = BuildDraft();
            if (!await ConfirmSendAsync(draft).ConfigureAwait(true))
            {
                return false;
            }

            return await SubmitAsync(draft, scheduledAt).ConfigureAwait(true);
        }
        finally
        {
            _sendFlowActive = false;
            OnPropertyChanged(nameof(CanSend));
        }
    }

    // true when something stops the send: the pictures and files still uploading, an address that is none, no recipient, no identity
    private async Task<bool> StopBeforeSendAsync()
    {
        if (Attachments.Any(a => a.State == AttachmentState.Uploading) || HasPendingInline())
        {
            _ = await AskAsync(new ComposerDialog(
                "pending-uploads",
                "Wait for the attachments to finish uploading",
                "The message goes out once every file and picture is uploaded.",
                [new DialogChoice("ok", "OK", DialogTone.Primary)])).ConfigureAwait(true);
            return true;
        }

        var invalid = RecipientText.InvalidAddresses([.. To.Recipients, .. Cc.Recipients, .. Bcc.Recipients]);
        if (invalid.Count > 0)
        {
            Notify("Check the address: " + string.Join(", ", invalid), error: true);
            return true;
        }

        if (RecipientText.Expand([.. To.Recipients, .. Cc.Recipients, .. Bcc.Recipients]).Count == 0)
        {
            Notify("Add at least one recipient.", error: true);
            return true;
        }

        if (_identityId is null && !(_fromOverride && _fromEmail.Contains('@', StringComparison.Ordinal)))
        {
            Notify("There is no sending identity for this account.", error: true);
            return true;
        }

        return false;
    }

    // the single dialog of Bulwark's send-warnings-dialog and external-recipient-confirm; the optional "send confirmation" setting asks even when nothing is wrong
    private async Task<bool> ConfirmSendAsync(ComposeDraft draft)
    {
        var warnings = SendWarnings.Check(draft, _c.Settings, _ownDomains).Where(w => w.Kind != SendWarningKind.ExternalRecipients || _c.ConfirmExternalRecipients).ToList();
        if (warnings.Count > 0)
        {
            var lines = warnings.SelectMany(WarningLines).ToList();
            var answer = await AskAsync(new ComposerDialog(
                "warnings",
                "Before you send",
                "Check this before the message goes out.",
                [new DialogChoice("send", "Send anyway", DialogTone.Primary), new DialogChoice("cancel", "Go back")],
                lines)).ConfigureAwait(true);
            return string.Equals(answer, "send", StringComparison.Ordinal);
        }

        if (_c.Settings.SendConfirmation)
        {
            var answer = await AskAsync(new ComposerDialog(
                "confirm",
                "Send this message?",
                "It goes to " + string.Join(", ", RecipientText.Expand(RecipientText.ParseList(draft.To)).Select(r => r.Email).Take(3)) + ".",
                [new DialogChoice("send", "Send", DialogTone.Primary), new DialogChoice("cancel", "Cancel")])).ConfigureAwait(true);
            return string.Equals(answer, "send", StringComparison.Ordinal);
        }

        return true;
    }

    private static IEnumerable<string> WarningLines(SendWarning w)
    {
        switch (w.Kind)
        {
            case SendWarningKind.EmptySubject:
                yield return "The subject is empty.";
                break;
            case SendWarningKind.UnfilledPlaceholders:
                yield return "Placeholders are still unfilled: " + string.Join(", ", w.Items);
                break;
            case SendWarningKind.MissingAttachment:
                yield return "The message mentions an attachment, but none is attached.";
                break;
            default:
                yield return "Recipients outside your own domains: " + string.Join(", ", w.Items);
                break;
        }
    }

    private async Task<bool> SubmitAsync(ComposeDraft draft, DateTimeOffset? scheduledAt)
    {
        IsSending = true;
        Status = scheduledAt is null ? "Sending…" : "Scheduling…";
        var gen = _autosave.Begin();
        var options = new SendOptions { UndoSeconds = scheduledAt is null ? _c.Settings.SendDelaySeconds : 0, ScheduledAt = scheduledAt };
        string? error = null;
        SendReceipt? receipt = null;
        try
        {
            var res = await _c.Sender.SendAsync(draft, options, CancellationToken.None).ConfigureAwait(true);
            receipt = res.IsOk ? res.Value : null;
            error = res.IsOk ? null : res.Error!.Message;
        }
#pragma warning disable CA1031 // a send that throws is a failed send: the composer stays open with the text intact
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            error = e.Message;
        }

        _ = _autosave.Finish(gen, null);
        IsSending = false;
        if (receipt is null)
        {
            if (_c.Sender.ShowsToasts)
            {
                // the coordinator toasts most refusals, but a quiet one (a cancelled question, a thrown error) would leave only "Not sent": keep the reason on screen (UI-SWEEP-1 C-05)
                Status = string.IsNullOrWhiteSpace(error) ? "Not sent" : "Not sent: " + error;
                return false;
            }

            Notify("The message was not sent: " + error, error: true);
            Status = "Not sent";
            return false;
        }

        ToastSent(receipt, scheduledAt);
        Finish(sent: true);
        return true;
    }

    // the undo window of the toast is send-pipeline's SendCoordinator when it is registered; this is the minimum so a held message can always be taken back
    private void ToastSent(SendReceipt receipt, DateTimeOffset? scheduledAt)
    {
        if (_c.Sender.ShowsToasts || _c.Toasts is not { } toasts)
        {
            return;
        }

        if (scheduledAt is { } at)
        {
            _ = toasts.Show(new ToastRequest("Scheduled for " + TimeZoneInfo.ConvertTime(at, (_c.DisplayZone?.Invoke() ?? _c.Time.LocalTimeZone)).ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture)));
            return;
        }

        _ = toasts.Show(receipt.CanUndo
            ? new ToastRequest("Message sent", false, "Undo", ct => UndoAsync(receipt, ct), TimeSpan.FromSeconds(Math.Max(_c.Settings.SendDelaySeconds, 3)))
            : new ToastRequest("Message sent"));
    }

    private async Task UndoAsync(SendReceipt receipt, CancellationToken ct)
    {
        var res = await _c.Sender.UndoAsync(receipt, ct).ConfigureAwait(true);
        if (!res.IsOk)
        {
            _ = _c.Toasts?.Show(new ToastRequest("Could not undo: " + res.Error!.Message, IsError: true));
            return;
        }

        if (_c.Reopen is { } reopen)
        {
            await reopen(res.Value!, ct).ConfigureAwait(true);
        }
    }

    private void Notify(string message, bool error)
    {
        Status = message;
        _ = _c.Toasts?.Show(new ToastRequest(message, error));
    }
}
