// Mirrors: Rust main.rs:430 restore_session (one client per login; its folders, list, identities and push hang off it); M1-plan §4.1.
// Owner jmap.

using Barbican.Core.Jmap;

namespace Barbican.Core.Mail.Service;

/// <summary>Makes the mail service and the sync engine of a connected login.</summary>
public interface IMailAccountFactory
{
    /// <summary>The read side over <paramref name="client"/>.</summary>
    MailService CreateService(IJmapClient client);

    /// <summary>The push and delta engine over <paramref name="service"/>.</summary>
    IMailSync CreateSync(MailService service);
}
