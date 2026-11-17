// Mirrors: Rust compose.rs:158 reply_identity, :246 resolve_reply_identity; Bulwark lib/reply-identity.ts findReplyIdentityId, resolveReplyFrom (catch-all, subAddressDelimiter). Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the body.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>Reply identity resolution (stub until stream send-pipeline lands).</summary>
public static class ReplyIdentity
{
    /// <summary>
    /// The identity for a reply or forward, in Bulwark's order: a reopened draft's own sender; the identity that sent a self-sent original; an own identity in To/Cc/Bcc (exact address, then without the
    /// sub-address tag); with <paramref name="autoSelect"/> (not on forwards) a catch-all address on a user's domain when <paramref name="matchMode"/> is "domain" (it also rewrites From); then Delivered-To;
    /// else null (the default identity).
    /// </summary>
    public static ReplyIdentityChoice? Resolve(IReadOnlyList<Identity> identities, ReplyFrom from, bool autoSelect, string matchMode, string subAddressDelimiter = "+") => throw new PortPendingException("ReplyIdentity.Resolve");
}
