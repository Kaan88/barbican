// Mirrors: Rust jmap.rs:1207 header_from, :1221 wildcard_covers, :1232 choose_identity, :1243 submission_envelope, :1251 mail_from_refused;
// Bulwark lib/jmap/client.ts (identities, From override). Stub from F2; owner jmap.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Submission;

/// <summary>The pure From and envelope rules of sending (M3 uses them; M1 ports and tests them).</summary>
public static class SubmissionRules
{
    /// <summary>The header From: the override when it has an address, else the identity (a wildcard identity names the login).</summary>
    public static EmailAddress HeaderFrom(Outgoing outgoing, Identity identity, string username) =>
        throw new PortPendingException("SubmissionRules.HeaderFrom: jmap stream");

    /// <summary>True when a <c>*@domain</c> identity covers the address (case-insensitive).</summary>
    public static bool WildcardCovers(string pattern, string address) =>
        throw new PortPendingException("SubmissionRules.WildcardCovers: jmap stream");

    /// <summary>The identity to send through: the one covering the override, else the composer's choice by id or email, else the first.</summary>
    public static Identity? ChooseIdentity(IReadOnlyList<Identity> identities, string from, string? fromOverride) =>
        throw new PortPendingException("SubmissionRules.ChooseIdentity: jmap stream");

    /// <summary>A custom envelope when MAIL FROM differs from the identity's or a hold is set; null when the default will do.</summary>
    public static SubmissionEnvelope? Envelope(string mailFrom, string identityMailFrom, long? holdSeconds, IReadOnlyList<string> recipients) =>
        throw new PortPendingException("SubmissionRules.Envelope: jmap stream");

    /// <summary>True for <c>forbiddenFrom</c> and <c>forbiddenMailFrom</c>.</summary>
    public static bool MailFromRefused(SetError error) => throw new PortPendingException("SubmissionRules.MailFromRefused: jmap stream");
}
