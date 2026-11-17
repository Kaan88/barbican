// Mirrors: Rust jmap.rs:721 scheduled (EmailSubmission/query undoStatus pending, then Email/get of the messages in chunks; receivedAt shown as sendAt), mail.rs:156 (the virtual "__scheduled" folder), mail.rs:619; Bulwark lib/jmap/client.ts listScheduledSubmissions.
// RFC 8621 section 7.3. Owner send-pipeline; the folder row and its list are shell views.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>The data of the Scheduled folder (id <see cref="FolderId"/>): the held messages, soonest first.</summary>
public sealed class ScheduledListSource(IAccountManager accounts, IScheduledSends scheduled)
{
    /// <summary>The virtual folder id (Rust "__scheduled").</summary>
    public const string FolderId = "__scheduled";

    public async Task<Result<IReadOnlyList<ScheduledItem>>> LoadAsync(string accountKey, CancellationToken ct)
    {
        var held = await scheduled.ListAsync(accountKey, ct).ConfigureAwait(false);
        if (!held.IsOk)
        {
            return Result<IReadOnlyList<ScheduledItem>>.Fail(held.Error!);
        }

        if (held.Value!.Count == 0)
        {
            return Result<IReadOnlyList<ScheduledItem>>.Ok([]);
        }

        if (accounts.SessionFor(accountKey)?.Client is not { } client)
        {
            return Result<IReadOnlyList<ScheduledItem>>.Fail(new ValidationError("The account is not signed in."));
        }

        var got = await client.GetAllAsync("Email/get", client.Session.MailAccountId, [.. held.Value.Select(h => h.EmailId)], JmapProperties.Json(JmapProperties.EmailList), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<IReadOnlyList<ScheduledItem>>.Fail(got.Error!);
        }

        var byId = got.Value!.Select(e => ModelReader.Summary(e)).OfType<EmailSummary>().ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);
        IReadOnlyList<ScheduledItem> items = [.. held.Value.Where(h => byId.ContainsKey(h.EmailId)).Select(h => new ScheduledItem(byId[h.EmailId] with { ReceivedAt = h.At }, h.SubmissionId, h.At))];
        return Result<IReadOnlyList<ScheduledItem>>.Ok(items);
    }
}
