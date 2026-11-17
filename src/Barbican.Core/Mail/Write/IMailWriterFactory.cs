// Mirrors: M2 plan section 4.3 (IMailWriterFactory: one writer per account session; accountId for shared accounts). Contract from stage F of M2; owner actions.

using Barbican.Core.Accounts;

namespace Barbican.Core.Mail.Write;

/// <summary>Makes the writer of an account session.</summary>
public interface IMailWriterFactory
{
    /// <summary>The writer for the session's own account, or for a shared account the session reaches.</summary>
    IMailWriter ForSession(IAccountSession session, string? accountId = null);
}
