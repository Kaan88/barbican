// Test double for IMailService: pages come from a script, every query is recorded, a gate can hold an answer back. Owner mailcore.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Tests.Support;

internal sealed class FakeMailService : IMailService
{
    public List<(ListQuery Query, ListCursor? After)> Queries { get; } = [];

    public MailboxTree Tree { get; set; } = new([]);

    public QuotaInfo? Quota { get; set; }

    public IReadOnlyList<Identity> Identities { get; set; } = [];

    /// <summary>Answers a query; the default returns an empty page.</summary>
    public Func<ListQuery, ListCursor?, CancellationToken, Task<Result<ListPage>>> OnQuery { get; set; } =
        (_, _, _) => Task.FromResult(Result<ListPage>.Ok(new ListPage()));

    public Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct)
    {
        Queries.Add((query, after));
        return OnQuery(query, after, ct);
    }

    public Task<Result<MailboxTree>> GetMailboxesAsync(CancellationToken ct) => Task.FromResult(Result<MailboxTree>.Ok(Tree));

    public Task<Result<EmailBody>> GetBodyAsync(string emailId, BodyFetchOptions options, CancellationToken ct) =>
        Task.FromResult(Result<EmailBody>.Fail(new TransportError("not in this fake", false)));

    public Task<Result<ConversationThread>> GetThreadAsync(string threadId, CancellationToken ct) =>
        Task.FromResult(Result<ConversationThread>.Ok(new ConversationThread(threadId, [])));

    public Task<Result<IReadOnlyList<Identity>>> GetIdentitiesAsync(CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<Identity>>.Ok(Identities));

    public Task<Result<QuotaInfo?>> GetQuotaAsync(CancellationToken ct) => Task.FromResult(Result<QuotaInfo?>.Ok(Quota));
}
