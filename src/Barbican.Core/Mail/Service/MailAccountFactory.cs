// Mirrors: Rust main.rs:430 restore_session; M1-plan §4.1. Owner jmap.

using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;

namespace Barbican.Core.Mail.Service;

/// <summary>The production <see cref="IMailAccountFactory"/>.</summary>
public sealed class MailAccountFactory(IHookBus hooks, TimeProvider time) : IMailAccountFactory
{
    /// <inheritdoc/>
    public MailService CreateService(IJmapClient client) => new(client, time);

    /// <inheritdoc/>
    public IMailSync CreateSync(MailService service) => new MailSync(service, hooks, time);
}
