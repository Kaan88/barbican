// Mirrors: Bulwark lib/jmap/client.ts sendEmail result, message-actions.ts (undo toast, "Send now"); RFC 8621 section 7. Contract from the M3 plan section 4.3; owner send-pipeline.
// Additive (reported): Draft (what the composer held, so an undo can reopen it even when the server copy could not go back to Drafts) and EnvelopeRewritten.

namespace Barbican.Core.Compose;

/// <summary>What the server accepted: enough to undo, send now, or tell a scheduled message in the Scheduled view.</summary>
public sealed record SendReceipt(string AccountId, string EmailId, string SubmissionId)
{
    /// <summary>When the held message is released (null for a send at once).</summary>
    public DateTimeOffset? ReleaseAt { get; init; }

    /// <summary>True while the submission is held and <c>undoStatus</c> can still become <c>canceled</c>.</summary>
    public bool CanUndo { get; init; }

    /// <summary>The composer's content at the send (the undo reopens it when the server copy cannot be restored).</summary>
    public ComposeDraft? Draft { get; init; }

    /// <summary>The server refused the From override as the envelope sender (forbiddenFrom) and the message went out with the identity's address as MAIL FROM; the header From is unchanged.</summary>
    public bool EnvelopeRewritten { get; init; }
}
