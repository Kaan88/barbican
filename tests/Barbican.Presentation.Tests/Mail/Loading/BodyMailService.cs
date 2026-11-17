// Test double for IMailService on the reader's side: bodies and threads from a dictionary, each answer optionally held back by a gate. Owner body.

using System.Collections.Concurrent;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Tests.Mail.Loading;

internal sealed class BodyMailService : IMailService
{
    public ConcurrentDictionary<string, EmailBody> Bodies { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, ConversationThread> Threads { get; } = new(StringComparer.Ordinal);

    /// <summary>Body requests held until the test releases them (by email id).</summary>
    public ConcurrentDictionary<string, TaskCompletionSource> Gates { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<(string Id, bool Prefetch)> BodyRequests { get; } = new();

    /// <summary>How long a held request takes to give up once cancelled (a real HTTP request aborts asynchronously, not inside Cancel()).</summary>
    public TimeSpan CancelLag { get; set; }

    public ConcurrentQueue<string> ThreadRequests { get; } = new();

    public TaskCompletionSource Hold(string id)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Gates[id] = gate;
        return gate;
    }

    public async Task<Result<EmailBody>> GetBodyAsync(string emailId, BodyFetchOptions options, CancellationToken ct)
    {
        BodyRequests.Enqueue((emailId, options.Prefetch));
        if (Gates.TryGetValue(emailId, out var gate))
        {
            try
            {
                await gate.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (CancelLag > TimeSpan.Zero)
            {
                await Task.Delay(CancelLag, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        return Bodies.TryGetValue(emailId, out var b)
            ? Result<EmailBody>.Ok(b)
            : Result<EmailBody>.Fail(new MethodError("Email/get", "notFound", "Message not found."));
    }

    public Task<Result<ConversationThread>> GetThreadAsync(string threadId, CancellationToken ct)
    {
        ThreadRequests.Enqueue(threadId);
        return Task.FromResult(Threads.TryGetValue(threadId, out var t) ? Result<ConversationThread>.Ok(t) : Result<ConversationThread>.Ok(new ConversationThread(threadId, [])));
    }

    public Task<Result<MailboxTree>> GetMailboxesAsync(CancellationToken ct) => Task.FromResult(Result<MailboxTree>.Ok(new MailboxTree([])));

    public Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct) => Task.FromResult(Result<ListPage>.Ok(new ListPage()));

    public Task<Result<IReadOnlyList<Identity>>> GetIdentitiesAsync(CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<Identity>>.Ok([]));

    public Task<Result<QuotaInfo?>> GetQuotaAsync(CancellationToken ct) => Task.FromResult(Result<QuotaInfo?>.Ok(null));
}
