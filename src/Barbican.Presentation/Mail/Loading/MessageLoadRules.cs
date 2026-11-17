// Mirrors: Rust msgload.rs (was_unseen :45, inline_bytes :50); no-flicker rules in PORT-PLAN.md §4.5. Stub from F2; owner body.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Small pure rules of loading a message for the reader.</summary>
public static class MessageLoadRules
{
    /// <summary>
    /// Whether a message was unread when it was opened, from the list's copy (the freshest) or the cached one; null when neither is known
    /// (decided from the loaded message). A null copy means "not an object" in Rust and is skipped.
    /// </summary>
    public static bool? WasUnseen(Keywords? listKeywords, Keywords? cachedKeywords) => throw new PortPendingException("MessageLoadRules.WasUnseen: body stream");

    /// <summary>Total size of the inline images a message's body refers to (parts with a cid and an image type).</summary>
    public static long InlineBytes(EmailBody message) => throw new PortPendingException("MessageLoadRules.InlineBytes: body stream");
}
