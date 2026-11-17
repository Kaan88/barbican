// Mirrors: Rust compose.rs:868-880 send, jmap.rs send_message/cancel_submission (:985 reschedule), Bulwark lib/jmap/client.ts sendEmail, cancelEmailSubmission, message-actions.ts; RFC 8621 section 7.5 (EmailSubmission/set, onSuccessUpdateEmail), RFC 4865. Stalwart crates/jmap/src/submission/set.rs. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>Submits a composed message. The caller (composer-ui) already ran the send warnings and the plugin send hooks.</summary>
public interface IMailSender
{
    /// <summary>Saves the draft, then EmailSubmission/set with the identity, an explicit envelope (with Bcc) when held, and HOLDFOR; onSuccessUpdateEmail files it in Sent.</summary>
    Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct);

    /// <summary>undoStatus canceled; the message returns to Drafts. Returns the seed to reopen it in a composer.</summary>
    Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct);

    /// <summary>"Send now" on the undo toast: releases the held submission at once.</summary>
    Task<Result<Unit>> SendNowAsync(SendReceipt receipt, CancellationToken ct);
}
