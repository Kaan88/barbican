// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:26-66; Barbican MailService(client, time, accountId) reads one JMAP account through one login.
using Barbican.Core.Accounts;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;

namespace Barbican.Core.Plugins.Api.Mail;

/// <summary>Production reader factory: the session's own service, or a service over the same login for a shared account.</summary>
public sealed class PluginMailReaders(TimeProvider time) : IPluginMailReaders
{
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    public IMailService Reader(IAccountSession session, string? sharedAccountId)
    {
        ArgumentNullException.ThrowIfNull(session);
        return sharedAccountId is null ? session.Mail : new MailService(session.Client, _time, sharedAccountId);
    }
}
