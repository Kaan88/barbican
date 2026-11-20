// Mirrors: Rust compose.rs:835-880 send_at (checks, warnings, hold, then send), mail.rs show_toast, schedule.rs:240-272 (Cancel send, "Sending cancelled. The message is in Drafts."); Bulwark components/email/email-composer.tsx (handleSend: pending images, address
// validation, send warnings), message-actions.ts (the undo-send toast with Undo and Send now, "Moved to Drafts" with Open, scheduled Cancel), components/email/send-warnings-dialog.tsx. M3 plan section 4.3 and I-282 (undo-send toast through IToastService).
// Order of the checks (as the composer's Send): another send running, inline images still uploading, entries that are no address, no recipient, then the warnings (one dialog), then IMailSender. Owner send-pipeline.

using System.ComponentModel;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Schedule;
using Barbican.Core.Editor;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>The send flow between the composer's Send button and the server: checks, the warnings dialog, the send with undo or schedule, and the toasts that follow (I-282).</summary>
public sealed class SendCoordinator(IMailSender sender, IScheduledSends scheduled, IIdentityService identities, IPrefsStore prefs, IToastService toasts, IComposerService composer, TimeProvider time) : INotifyPropertyChanged
{
    private readonly Lock _gate = new();
    private TaskCompletionSource<bool>? _decision;
    private bool _isSending;

    /// <summary>Warnings were found; the host shows the dialog and answers with <see cref="Confirm"/> or <see cref="Cancel"/>. Without a subscriber the send goes on (nobody can be asked: a headless run).</summary>
    public event EventHandler<WarningsRaisedEventArgs>? WarningsRaised;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A send is running (the Send button is disabled).</summary>
    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (_isSending != value)
            {
                _isSending = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSending)));
            }
        }
    }

    /// <summary>The user chose "Send anyway".</summary>
    public void Confirm() => Decide(true);

    /// <summary>The user chose Cancel in the warnings dialog.</summary>
    public void Cancel() => Decide(false);

    public async Task<SendOutcome> RunAsync(ComposeDraft draft, SendMode mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(mode);
        lock (_gate)
        {
            if (IsSending)
            {
                return new SendOutcome(SendOutcomeStatus.Busy) { Message = "A message is already being sent." };
            }

            IsSending = true;
        }

        try
        {
            return await RunCoreAsync(draft, mode, ct).ConfigureAwait(false);
        }
        finally
        {
            IsSending = false;
        }
    }

    private static bool HasPendingImages(Doc body) =>
        body.Blocks.Any(b => b.Kind is BlockKind.Image { Data.BlobId: null });

    private async Task<SendOutcome> RunCoreAsync(ComposeDraft draft, SendMode mode, CancellationToken ct)
    {
        if (HasPendingImages(draft.Body) && !draft.PlainText)
        {
            return Refuse(SendOutcomeStatus.PendingImages, "An image is still uploading. Try again in a moment.");
        }

        var bad = new[] { draft.To, draft.Cc, draft.Bcc }.SelectMany(ComposeRules.InvalidAddresses).ToList();
        if (bad.Count > 0)
        {
            var named = string.Join(", ", bad);
            toasts.Show(new ToastRequest($"Not an address: {named}.", true));
            return new SendOutcome(SendOutcomeStatus.InvalidAddresses) { InvalidAddresses = bad, Message = $"Not an address: {named}." };
        }

        if (new[] { draft.To, draft.Cc, draft.Bcc }.All(f => f.Trim().Length == 0))
        {
            return Refuse(SendOutcomeStatus.Failed, "Add at least one recipient.");
        }

        var settings = ComposeSettings.Read(prefs.Current);
        if (await AskAsync(draft, settings, mode, ct).ConfigureAwait(false) is false)
        {
            return new SendOutcome(SendOutcomeStatus.Cancelled);
        }

        var options = new SendOptions { UndoSeconds = settings.SendDelaySeconds, ScheduledAt = mode.ScheduledAt, ClassificationChoice = mode.ClassificationChoice };
        var sent = await sender.SendAsync(draft, options, ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Refuse(SendOutcomeStatus.Failed, sent.Error!.Message) with { Error = sent.Error };
        }

        var receipt = sent.Value!;
        if (mode.ScheduledAt is { } at)
        {
            ToastScheduled(draft.AccountId, receipt, at);
            return new SendOutcome(SendOutcomeStatus.Scheduled) { Receipt = receipt };
        }

        ToastSent(receipt);
        return new SendOutcome(SendOutcomeStatus.Sent) { Receipt = receipt };
    }

    private async Task<bool> AskAsync(ComposeDraft draft, ComposeSettings settings, SendMode mode, CancellationToken ct)
    {
        var domains = await OwnDomainsAsync(draft.AccountId, ct).ConfigureAwait(false);
        var warnings = SendWarnings.Check(draft, settings, domains);
        if (warnings.Count == 0 || WarningsRaised is not { } raise)
        {
            return true;
        }

        TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _decision = decision;
        }

        raise(this, new WarningsRaisedEventArgs(warnings, mode));
        using var registration = ct.Register(() => decision.TrySetResult(false));
        return await decision.Task.ConfigureAwait(false);
    }

    private void Decide(bool answer)
    {
        lock (_gate)
        {
            _decision?.TrySetResult(answer);
            _decision = null;
        }
    }

    private async Task<IReadOnlyList<string>> OwnDomainsAsync(string accountId, CancellationToken ct)
    {
        var list = await identities.ListAsync(accountId, ct).ConfigureAwait(false);
        return list.IsOk
            ? [.. list.Value!.Select(i => i.Identity.Email).Select(e => e.LastIndexOf('@') is var at and >= 0 ? e[(at + 1)..].Trim().ToLowerInvariant() : "").Where(d => d.Length > 0).Distinct(StringComparer.Ordinal)]
            : [];
    }

    private SendOutcome Refuse(SendOutcomeStatus status, string text)
    {
        toasts.Show(new ToastRequest(text, true));
        return new SendOutcome(status) { Message = text };
    }

    private void ToastSent(SendReceipt receipt)
    {
        if (!receipt.CanUndo)
        {
            toasts.Show(new ToastRequest("Message sent."));
            return;
        }

        // the toast stays for the undo window: Undo reopens the message in the composer, Send now releases it at once
        var window = receipt.ReleaseAt is { } r && r > time.GetUtcNow() ? r - time.GetUtcNow() : ToastService.UndoDuration;
        toasts.Show(new ToastRequest("Sending…", false, "Undo", ct => UndoAsync(receipt, ct), window)
        {
            SecondaryLabel = "Send now",
            SecondaryAction = ct => SendNowAsync(receipt, ct),
        });
    }

    private async Task UndoAsync(SendReceipt receipt, CancellationToken ct)
    {
        var seed = await sender.UndoAsync(receipt, ct).ConfigureAwait(false);
        if (seed.IsOk)
        {
            await composer.OpenSeedAsync(seed.Value!, ct).ConfigureAwait(false);
        }
        else
        {
            toasts.Show(new ToastRequest($"Couldn't undo: {seed.Error!.Message}", true));
        }
    }

    private async Task SendNowAsync(SendReceipt receipt, CancellationToken ct)
    {
        var r = await sender.SendNowAsync(receipt, ct).ConfigureAwait(false);
        toasts.Show(r.IsOk ? new ToastRequest("Message sent.") : new ToastRequest(r.Error!.Message, true));
    }

    private void ToastScheduled(string accountId, SendReceipt receipt, DateTimeOffset at)
    {
        var label = ScheduleRules.Label(at, string.Equals(prefs.Current.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal), time.GetUtcNow(), Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(prefs.Current.Str("timeZone", "auto"), time.LocalTimeZone));
        toasts.Show(new ToastRequest($"Scheduled for {label}.", false, "Cancel", ct => CancelScheduledAsync(accountId, receipt.SubmissionId, ct)));
    }

    private async Task CancelScheduledAsync(string accountId, string submissionId, CancellationToken ct)
    {
        var r = await scheduled.CancelAsync(accountId, submissionId, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            toasts.Show(new ToastRequest($"Couldn't cancel the scheduled send: {r.Error!.Message}", true));
            return;
        }

        var emailId = r.Value!;
        toasts.Show(new ToastRequest("Sending cancelled. Moved to Drafts.", false, "Open", token => composer.OpenAsync(new ComposeRequest(accountId, ComposeKind.EditDraft) { SourceEmailId = emailId }, token)));
    }
}
