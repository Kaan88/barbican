// Mirrors: see IMailSender. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="IMailSender"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class MailSender : IMailSender
{
    public Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct) => Task.FromException<Result<SendReceipt>>(new PortPendingException("MailSender.SendAsync"));

    public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct) => Task.FromException<Result<ComposeSeed>>(new PortPendingException("MailSender.UndoAsync"));

    public Task<Result<Unit>> SendNowAsync(SendReceipt receipt, CancellationToken ct) => Task.FromException<Result<Unit>>(new PortPendingException("MailSender.SendNowAsync"));
}
