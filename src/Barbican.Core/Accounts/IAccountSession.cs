// Mirrors: M1-plan §4.2. Contract stub from F2; owner accounts.

using Barbican.Core.Jmap;
using Barbican.Core.Mail;

namespace Barbican.Core.Accounts;

/// <summary>The live objects of one signed-in account.</summary>
public interface IAccountSession
{
    AccountInfo Account { get; }

    IJmapClient Client { get; }

    IMailService Mail { get; }

    IMailSync Sync { get; }
}
