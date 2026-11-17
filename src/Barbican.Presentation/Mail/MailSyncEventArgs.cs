// Mirrors: M1-plan §4.1 (MailSyncEvent). Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>A sync event on its way to the subscribers of <see cref="MailEventHub"/>.</summary>
/// <param name="syncEvent">The event.</param>
/// <param name="accountKey">The account whose engine produced it (null: not known); ids repeat across accounts, so a subscriber that files the event by account must use this key, never "the active account now".</param>
public sealed class MailSyncEventArgs(MailSyncEvent syncEvent, string? accountKey = null) : EventArgs
{
    public MailSyncEvent Event { get; } = syncEvent;

    /// <summary>The account the event belongs to.</summary>
    public string? AccountKey { get; } = accountKey;
}
