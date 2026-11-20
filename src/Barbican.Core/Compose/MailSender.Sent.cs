// Mirrors Bulwark lib/plugin-after-send.ts: notify on acceptance or after an undo hold releases.
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Compose;

public sealed partial class MailSender
{
    private readonly Lock _sentGate = new();
    private readonly Dictionary<(string Account, string Submission), HeldNotification> _heldNotifications = [];
    private readonly HashSet<(string Account, string Submission)> _heldChanges = [];
    private readonly CancellationTokenSource _sentStop = new();
    private bool _sentDisposed;

    private enum HeldOutcome { Pending, Reconcile, Drop, Released }

    private sealed record HeldNotification(EmailSentPayload Payload, IOutgoingAddOnSend? AddOns, ComposeContext Context, ITimer Timer)
    {
        public bool Elapsed { get; set; }
        public bool NeedsConfirmation { get; set; }
        public bool Checking { get; set; }
    }

    private sealed class HeldChange(Action<HeldOutcome, string?> complete) : IDisposable
    {
        public HeldOutcome Outcome { get; set; }
        public string? NewSubmissionId { get; set; }

        public void Dispose() => complete(Outcome, NewSubmissionId);
    }

    private void AnnounceAccepted(ComposeContext ctx, SendReceipt receipt, ComposeDraft draft, DateTimeOffset? scheduledAt, IOutgoingAddOnSend? addOnSend)
    {
        var payload = new EmailSentPayload(receipt.EmailId, receipt.AccountId, receipt.SubmissionId,
            draft.To, draft.Cc, draft.Bcc, draft.Subject, scheduledAt is null ? null : receipt.ReleaseAt ?? scheduledAt)
        {
            JmapAccountId = ctx.Account,
            ComposerId = draft.ComposerId,
        };
        if (scheduledAt is null && receipt.ReleaseAt is { } release) ScheduleSent(payload, addOnSend, ctx, release);
        else NotifySent(payload, addOnSend);
    }

    private void ScheduleSent(EmailSentPayload payload, IOutgoingAddOnSend? addOnSend, ComposeContext ctx, DateTimeOffset release)
    {
        var key = (payload.AccountId, payload.SubmissionId);
        lock (_sentGate)
        {
            if (_sentDisposed || _heldNotifications.ContainsKey(key)) return;
            var timer = time.CreateTimer(_ => ExpireSent(key), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _heldNotifications.Add(key, new HeldNotification(payload, addOnSend, ctx, timer));
            var delay = release - time.GetUtcNow();
            _ = timer.Change(delay > TimeSpan.Zero ? delay : TimeSpan.FromTicks(1), Timeout.InfiniteTimeSpan);
        }
    }

    private void ExpireSent((string Account, string Submission) key) => CompleteHeld(key, HeldOutcome.Pending, null, endChange: false);

    private HeldChange? TryChangeHeld(SendReceipt receipt)
    {
        var key = (receipt.AccountId, receipt.SubmissionId);
        lock (_sentGate)
        {
            if (_sentDisposed || !_heldChanges.Add(key)) return null;
        }
        return new HeldChange((outcome, submission) => CompleteHeld(key, outcome, submission, endChange: true));
    }

    private void CompleteHeld((string Account, string Submission) key, HeldOutcome outcome, string? newSubmission, bool endChange)
    {
        HeldNotification? entry;
        CancellationToken? verify = null;
        lock (_sentGate)
        {
            if (endChange) _ = _heldChanges.Remove(key);
            if (_sentDisposed || !_heldNotifications.TryGetValue(key, out entry)) return;
            if (!endChange) entry.Elapsed = true;
            if (outcome == HeldOutcome.Reconcile)
            {
                entry.NeedsConfirmation = true;
                outcome = HeldOutcome.Pending;
            }
            if (_heldChanges.Contains(key) || (outcome == HeldOutcome.Pending && !entry.Elapsed)) return;
            if (outcome == HeldOutcome.Pending && entry.NeedsConfirmation)
            {
                if (entry.Checking) return;
                entry.Checking = true;
                verify = _sentStop.Token;
            }
            else _ = _heldNotifications.Remove(key);
        }
        if (verify is { } ct)
        {
            _ = ReconcileHeldAsync(key, entry, ct);
            return;
        }
        entry.Timer.Dispose();
        if (outcome == HeldOutcome.Drop) return;
        var payload = newSubmission is null ? entry.Payload : entry.Payload with { SubmissionId = newSubmission };
        NotifySent(payload, entry.AddOns);
    }

    private async Task ReconcileHeldAsync((string Account, string Submission) key, HeldNotification entry, CancellationToken ct)
    {
        HeldOutcome outcome;
        try { outcome = await ReadHeldOutcomeAsync(entry.Context, key.Submission, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
#pragma warning disable CA1031 // Failed verification keeps the event pending while the sending session is current.
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { outcome = HeldOutcome.Reconcile; }
#pragma warning restore CA1031
        lock (_sentGate)
        {
            if (_sentDisposed || !_heldNotifications.TryGetValue(key, out var current) || !ReferenceEquals(current, entry)) return;
            entry.Checking = false;
            if (outcome == HeldOutcome.Reconcile)
            {
                _ = entry.Timer.Change(TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
                return;
            }
        }
        CompleteHeld(key, outcome, null, endChange: false);
    }

    private async Task<HeldOutcome> ReadHeldOutcomeAsync(ComposeContext ctx, string submissionId, CancellationToken ct)
    {
        if (!ReferenceEquals(accounts.SessionFor(ctx.Session.Account.Key), ctx.Session)) return HeldOutcome.Drop;
        var status = await SubmissionOps.GetAsync(ctx, submissionId, ct).ConfigureAwait(false);
        return status.IsOk ? status.Value.Str("undoStatus") switch
        {
            "pending" => HeldOutcome.Reconcile,
            "final" => HeldOutcome.Released,
            _ => HeldOutcome.Drop,
        } : HeldOutcome.Reconcile;
    }

    private void NotifySent(EmailSentPayload payload, IOutgoingAddOnSend? addOnSend)
    {
#pragma warning disable CA1031 // Observer failures cannot turn an accepted send into an apparent failure.
        try
        {
            if (hooks.HasHandlers(HookNames.OnAfterEmailSend)) hooks.Fire(HookNames.OnAfterEmailSend, () => payload);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
        try { addOnSend?.NotifySent(payload); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
#pragma warning restore CA1031
    }

    public void Dispose()
    {
        HeldNotification[] pending;
        lock (_sentGate)
        {
            if (_sentDisposed) return;
            _sentDisposed = true;
            pending = [.. _heldNotifications.Values];
            _heldNotifications.Clear();
            _heldChanges.Clear();
        }
        _sentStop.Cancel();
        _sentStop.Dispose();
        foreach (var entry in pending) entry.Timer.Dispose();
    }
}
