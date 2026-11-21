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
    private int _proofreadingWarningCount;
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

    /// <summary>Identifies the send input while the composer is submitting it.</summary>
    public string? SendAttemptRevision => IsSending ? PluginRevision : null;

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

    /// <summary>Updates the advisory count from current checker results shown by the composer.</summary>
    public void SetProofreadingWarningCount(int count) => _proofreadingWarningCount = Math.Max(0, count);

    internal Task<bool> SendInlineAsync(CancellationToken ct) => SendAtAsync(null, ct);

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

    private async Task<bool> SendAtAsync(DateTimeOffset? scheduledAt, CancellationToken ct = default)
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
            var draft = BuildDraft() with
            {
                ComposerId = Id,
                AuthoredText = PluginBodyText(),
                IsQuickReply = _c.AskInline is not null,
                Body = CurrentDoc().Clone(),
            };
            var revision = PluginRevision;
            if (!await ConfirmSendAsync(draft, scheduledAt is not null).ConfigureAwait(true))
            {
                return false;
            }
            if (!string.Equals(revision, PluginRevision, StringComparison.Ordinal))
            {
                Notify("The message changed. Review it before sending again.", error: true);
                return false;
            }

            ct.ThrowIfCancellationRequested();
            return await SubmitAsync(draft, scheduledAt, ct).ConfigureAwait(true);
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

    // Bulwark's two questions before a send, asked in this order: send-warnings-dialog.tsx (one dialog for everything it found; a single warning keeps its own title and text) and
    // external-recipient-confirm.tsx (every address outside the own domains, once per send); the optional "send confirmation" setting asks only when nothing else did.
    // Every question must be answered with its go-ahead button: Escape, Back, a closed composer or any other answer sends nothing (the callers treat false as "not sent").
    private async Task<bool> ConfirmSendAsync(ComposeDraft draft, bool scheduling)
    {
        var warnings = SendWarnings.Check(draft, _c.Settings, _ownDomains);
        var general = warnings.Where(w => w.Kind != SendWarningKind.ExternalRecipients).ToList();
        var external = _c.ConfirmExternalRecipients ? warnings.FirstOrDefault(w => w.Kind == SendWarningKind.ExternalRecipients) : null;
        var asked = false;
        if (general.Count > 0 || _proofreadingWarningCount > 0)
        {
            asked = true;
            if (!await AskWarningsAsync(general, scheduling).ConfigureAwait(true))
            {
                return false;
            }
        }

        if (external is not null)
        {
            asked = true;
            if (!await AskExternalAsync(draft, external.Items, scheduling).ConfigureAwait(true))
            {
                return false;
            }
        }

        if (!asked && _c.Settings.SendConfirmation)
        {
            var answer = await AskAsync(new ComposerDialog(
                "confirm",
                scheduling ? "Schedule this message?" : "Send this message?",
                "It goes to " + string.Join(", ", RecipientText.Expand(RecipientText.ParseList(draft.To)).Select(r => r.Email).Take(3)) + ".",
                [new DialogChoice("send", scheduling ? "Schedule" : "Send", DialogTone.Primary), new DialogChoice("cancel", "Cancel")])).ConfigureAwait(true);
            return string.Equals(answer, "send", StringComparison.Ordinal);
        }

        return true;
    }

    // send-warnings-dialog.tsx:63-119 and the English texts of locales/en/common.json:1148-1170 (email_composer), :4272-4289 (compose_send), :7384-7389 (proofreading.send_warning)
    private async Task<bool> AskWarningsAsync(List<SendWarning> general, bool scheduling)
    {
        var proofreading = _proofreadingWarningCount;
        var sendLabel = scheduling ? "Schedule anyway" : "Send anyway";
        var question = general.Count + (proofreading > 0 ? 1 : 0) == 1
            ? SingleWarning(general.FirstOrDefault(), proofreading, sendLabel)
            : ManyWarnings(general, proofreading, scheduling, sendLabel);
        var answer = await AskAsync(question).ConfigureAwait(true);
        return string.Equals(answer, "send", StringComparison.Ordinal);
    }

    // one warning keeps its own title and text (send-warnings-dialog.tsx:69-92); null is the proofreading count on its own
    private static ComposerDialog SingleWarning(SendWarning? only, int proofreading, string sendLabel)
    {
        var back = "Back to editing";
        IReadOnlyList<string> chips = [];
        string title;
        string message;
        switch (only?.Kind)
        {
            case SendWarningKind.EmptySubject:
                (title, message) = ("Send without a subject?", "This message has no subject. Send it anyway?");
                break;
            case SendWarningKind.UnfilledPlaceholders:
                (title, message) = ("Check placeholders?", "Your message has placeholder text that recipients will see. Do you want to edit it before sending?");
                chips = only.Items;
                break;
            case SendWarningKind.MissingAttachment:
                (title, message) = ("Did you forget an attachment?", $"Your message mentions \"{string.Join(", ", only.Items)}\" but no file is attached. Send anyway?");
                break;
            default:
                (title, message) = ("Send with open issues?", proofreading == 1 ? "1 spelling or grammar issue is still open in this message." : $"{proofreading} spelling or grammar issues are still open in this message.");
                back = "Review";
                break;
        }

        return new ComposerDialog("warnings", title, message, [new DialogChoice("send", sendLabel, DialogTone.Primary), new DialogChoice("cancel", back)], null, chips);
    }

    // several warnings share one dialog, each a bullet (send-warnings-dialog.tsx:93-116)
    private static ComposerDialog ManyWarnings(List<SendWarning> general, int proofreading, bool scheduling, string sendLabel)
    {
        IReadOnlyList<string> chips = [];
        var lines = new List<string>();
        foreach (var w in general)
        {
            switch (w.Kind)
            {
                case SendWarningKind.EmptySubject:
                    lines.Add("No subject");
                    break;
                case SendWarningKind.UnfilledPlaceholders:
                    lines.Add("Placeholder text that recipients will see:");
                    chips = w.Items;
                    break;
                default:
                    lines.Add($"Mentions \"{string.Join(", ", w.Items)}\", but no file is attached");
                    break;
            }
        }

        if (proofreading > 0)
        {
            lines.Add("Open spelling or grammar issues: " + proofreading.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return new ComposerDialog(
            "warnings",
            scheduling ? "Schedule this message anyway?" : "Send this message anyway?",
            string.Empty,
            [new DialogChoice("send", sendLabel, DialogTone.Primary), new DialogChoice("cancel", "Back to editing")],
            lines,
            chips);
    }

    // external-recipient-confirm.tsx:201-250 (title, count message, To/Cc/Bcc list, Back first and Send anyway second); the lines read "To: address"
    private async Task<bool> AskExternalAsync(ComposeDraft draft, IReadOnlyList<string> addresses, bool scheduling)
    {
        string FieldOf(string address)
        {
            bool In(string list) => RecipientText.Expand(RecipientText.ParseList(list)).Any(r => string.Equals(r.Email, address, StringComparison.OrdinalIgnoreCase));
            return In(draft.To) ? "To" : In(draft.Cc) ? "Cc" : "Bcc";
        }

        var answer = await AskAsync(new ComposerDialog(
            "external",
            "Send outside your organisation?",
            addresses.Count == 1 ? "This recipient is outside your organisation. Send only if you mean to." : $"These {addresses.Count} recipients are outside your organisation. Send only if you mean to.",
            [new DialogChoice("send", scheduling ? "Schedule anyway" : "Send anyway", DialogTone.Primary), new DialogChoice("cancel", "Back")],
            [.. addresses.Select(a => FieldOf(a) + ": " + a)])).ConfigureAwait(true);
        return string.Equals(answer, "send", StringComparison.Ordinal);
    }

    private async Task<bool> SubmitAsync(ComposeDraft draft, DateTimeOffset? scheduledAt, CancellationToken ct)
    {
        IsSending = true;
        Status = scheduledAt is null ? "Sending…" : "Scheduling…";
        var gen = _autosave.Begin();
        draft = draft with { ComposerRevision = SendAttemptRevision };
        var options = new SendOptions
        {
            UndoSeconds = scheduledAt is null ? _c.Settings.SendDelaySeconds : 0,
            ScheduledAt = scheduledAt,
            ClassificationChoice = ClassificationChoice,
        };
        string? error = null;
        var unknown = false;
        SendReceipt? receipt = null;
        try
        {
            var res = await _c.Sender.SendAsync(draft, options, ct).ConfigureAwait(true);
            receipt = res.IsOk ? res.Value : null;
            error = res.IsOk ? null : res.Error!.Message;
            unknown = res.Error is RawMessageOutcomeUnknown;
        }
#pragma warning disable CA1031 // a send that throws is a failed send: the composer stays open with the text intact
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "Send cancelled.";
        }
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            error = e.Message;
        }

        _ = _autosave.Finish(gen, null);
        IsSending = false;
        if (receipt is null)
        {
            if (unknown)
            {
                Status = error!;
                if (!_c.Sender.ShowsToasts) Notify(Status, error: true);
                return false;
            }
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
