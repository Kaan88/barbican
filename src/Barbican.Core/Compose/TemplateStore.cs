// Mirrors: see ITemplateStore. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="ITemplateStore"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class TemplateStore : ITemplateStore
{
    public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountId, CancellationToken ct) => Task.FromException<Result<IReadOnlyList<MailTemplate>>>(new PortPendingException("TemplateStore.ListAsync"));

    public Task<Result<MailTemplate>> CreateAsync(string accountId, string name, string subject, string body, bool isHtml, CancellationToken ct) => Task.FromException<Result<MailTemplate>>(new PortPendingException("TemplateStore.CreateAsync"));

    public Task<Result<Unit>> DeleteAsync(string accountId, string templateId, CancellationToken ct) => Task.FromException<Result<Unit>>(new PortPendingException("TemplateStore.DeleteAsync"));
}
