// Test double: the real mail service, with a hook that runs when a call starts (so a test can revoke a grant or change the session while the server "answers").
using Barbican.Core.Common;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class HookedMail(IMailService inner, Func<Action?> hook) : IMailService
{
    public Task<Result<MailboxTree>> GetMailboxesAsync(CancellationToken ct) { hook()?.Invoke(); return inner.GetMailboxesAsync(ct); }

    public Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct) { hook()?.Invoke(); return inner.QueryAsync(query, after, ct); }

    public Task<Result<EmailBody>> GetBodyAsync(string emailId, BodyFetchOptions options, CancellationToken ct) { hook()?.Invoke(); return inner.GetBodyAsync(emailId, options, ct); }

    public Task<Result<ConversationThread>> GetThreadAsync(string threadId, CancellationToken ct) { hook()?.Invoke(); return inner.GetThreadAsync(threadId, ct); }

    public Task<Result<IReadOnlyList<Identity>>> GetIdentitiesAsync(CancellationToken ct) { hook()?.Invoke(); return inner.GetIdentitiesAsync(ct); }

    public Task<Result<QuotaInfo?>> GetQuotaAsync(CancellationToken ct) { hook()?.Invoke(); return inner.GetQuotaAsync(ct); }
}
