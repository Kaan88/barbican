// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:26-66 (the message is read from the account the plugin names or the signed-in
// login's own; shared accounts only through that login). Host seam: the mail service of one captured session and JMAP account.
using Barbican.Core.Accounts;
using Barbican.Core.Mail;

namespace Barbican.Core.Plugins.Api.Mail;

/// <summary>Resolves the read side of mail for a captured session, never for an arbitrary login.</summary>
public interface IPluginMailReaders
{
    /// <summary>The mail service of <paramref name="session"/>; <paramref name="sharedAccountId"/> null is the login's own account.</summary>
    IMailService Reader(IAccountSession session, string? sharedAccountId);
}
