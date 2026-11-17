// Mirrors: M1-plan §4.1 (MailSyncEvent). Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>A sync event on its way to the subscribers of <see cref="MailEventHub"/>.</summary>
public sealed class MailSyncEventArgs(MailSyncEvent syncEvent) : EventArgs
{
    public MailSyncEvent Event { get; } = syncEvent;
}
