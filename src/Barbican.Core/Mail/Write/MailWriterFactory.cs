// Mirrors: Bulwark lib/jmap/client.ts (every write takes an optional accountId for shared accounts, e.g. batchDeleteEmails :4090).

using Barbican.Core.Accounts;

namespace Barbican.Core.Mail.Write;

/// <summary>Builds the <see cref="MailWriter"/> of a session's own account or of a shared account it reaches.</summary>
public sealed class MailWriterFactory : IMailWriterFactory
{
    /// <inheritdoc />
    public IMailWriter ForSession(IAccountSession session, string? accountId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new MailWriter(session.Client, string.IsNullOrEmpty(accountId) ? session.Client.Session.MailAccountId : accountId);
    }
}
