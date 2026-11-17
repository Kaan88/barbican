// Mirrors: RFC 8621 section 4.7 (Email/copy: id, mailboxIds, keywords); Bulwark lib/jmap/client.ts copyEmailAcrossAccounts :12278; G-048. Contract from stage F of M2; owner actions.

namespace Barbican.Core.Mail.Write;

/// <summary>One message to copy into another account.</summary>
public sealed record EmailCopyEntry(string EmailId, IReadOnlyList<string> MailboxIds, IReadOnlyList<string> Keywords);
