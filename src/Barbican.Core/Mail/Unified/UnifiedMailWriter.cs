// Mirrors: Rust unified.rs:474 shared_apply (an action on rows of another account goes out with that account's id: keywords as a patch,
// delete/archive/spam to that account's folder of the role), :456 match_mailbox and :466 resolve_moveto (a folder picked in the active
// account is found in the other one by role, else by name), mail.rs apply (rows grouped by their source). M2 plan 6.5: an action on a
// unified row writes only to its own account. Owner unified.
// Ids that are not unified row ids go to the writer of the account on screen unchanged; row ids are grouped by source, sent to that source's
// writer with their server ids, and the report names the row ids again. Folder-level calls (empty, create, upload, import) are the account
// on screen's.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;

namespace Barbican.Core.Mail.Unified;

/// <summary>An <see cref="IMailWriter"/> that sends each unified row to the account it came from.</summary>
/// <param name="own">The writer of the account on screen.</param>
/// <param name="writerOf">The writer of a source (login and shared account of a row), or null when that login is not signed in.</param>
/// <param name="mapMailbox">A folder id of the account on screen as the matching folder id of a source (role, else name), or null.</param>
public sealed class UnifiedMailWriter(
    IMailWriter own,
    Func<UnifiedRowId, IMailWriter?> writerOf,
    Func<UnifiedRowId, string, CancellationToken, Task<string?>> mapMailbox) : IMailWriter
{
    /// <inheritdoc />
    public Task<Result<SetReport>> UpdateKeywordsAsync(IReadOnlyList<string> emailIds, IReadOnlyDictionary<string, bool> patch, CancellationToken ct) =>
        RouteAsync(emailIds, (w, ids, _, t) => w.UpdateKeywordsAsync(ids, patch, t), null, ct);

    /// <inheritdoc />
    public Task<Result<SetReport>> MoveAsync(IReadOnlyList<string> emailIds, string? fromMailboxId, string toMailboxId, CancellationToken ct) =>
        RouteAsync(emailIds, (w, ids, map, t) => w.MoveAsync(ids, fromMailboxId is null ? null : map!.GetValueOrDefault(fromMailboxId), map![toMailboxId]!, t), [toMailboxId, .. fromMailboxId is null ? [] : new[] { fromMailboxId }], ct);

    /// <inheritdoc />
    public Task<Result<SetReport>> MoveWithKeywordsAsync(IReadOnlyList<string> emailIds, string toMailboxId, IReadOnlyDictionary<string, bool> keywords, CancellationToken ct) =>
        RouteAsync(emailIds, (w, ids, map, t) => w.MoveWithKeywordsAsync(ids, map![toMailboxId]!, keywords, t), [toMailboxId], ct);

    /// <inheritdoc />
    public Task<Result<SetReport>> RemoveFromMailboxAsync(IReadOnlyList<string> emailIds, string mailboxId, CancellationToken ct) =>
        RouteAsync(emailIds, (w, ids, map, t) => w.RemoveFromMailboxAsync(ids, map![mailboxId]!, t), [mailboxId], ct);

    /// <inheritdoc />
    public Task<Result<SetReport>> DestroyAsync(IReadOnlyList<string> emailIds, CancellationToken ct) =>
        RouteAsync(emailIds, (w, ids, _, t) => w.DestroyAsync(ids, t), null, ct);

    /// <inheritdoc />
    public async Task<Result<SetReport>> SetMailboxIdsAsync(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> mailboxIdsById, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mailboxIdsById);
        if (mailboxIdsById.Keys.All(k => UnifiedRows.ParseRow(k) is null))
        {
            return await own.SetMailboxIdsAsync(mailboxIdsById, ct).ConfigureAwait(false);
        }

        // whole folder sets are the account's own folder ids already (a labels edit is made in the row's account): only the ids change
        return await RouteAsync(
            [.. mailboxIdsById.Keys],
            (w, ids, _, t) => w.SetMailboxIdsAsync(ids.ToDictionary(i => i, i => mailboxIdsById.First(kv => string.Equals(UnifiedRows.RealId(kv.Key), i, StringComparison.Ordinal)).Value, StringComparer.Ordinal), t),
            null,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<int>> EmptyMailboxAsync(string mailboxId, CancellationToken ct) => own.EmptyMailboxAsync(mailboxId, ct);

    /// <inheritdoc />
    public Task<Result<int>> MarkMailboxReadAsync(string mailboxId, bool withChildren, CancellationToken ct) => own.MarkMailboxReadAsync(mailboxId, withChildren, ct);

    /// <inheritdoc />
    public Task<Result<Mailbox>> CreateMailboxAsync(string name, string? parentId, string? role, CancellationToken ct) => own.CreateMailboxAsync(name, parentId, role, ct);

    /// <inheritdoc />
    public Task<Result<SetReport>> UpdateMailboxesAsync(IReadOnlyDictionary<string, MailboxPatch> patches, CancellationToken ct) => own.UpdateMailboxesAsync(patches, ct);

    /// <inheritdoc />
    public Task<Result<Unit>> DestroyMailboxAsync(string mailboxId, bool removeEmails, CancellationToken ct) => own.DestroyMailboxAsync(mailboxId, removeEmails, ct);

    /// <inheritdoc />
    public Task<Result<UploadedBlob>> UploadAsync(ReadOnlyMemory<byte> data, string type, CancellationToken ct) => own.UploadAsync(data, type, ct);

    /// <inheritdoc />
    public Task<Result<EmailImportReport>> ImportAsync(IReadOnlyList<EmailImportEntry> entries, CancellationToken ct) => own.ImportAsync(entries, ct);

    /// <inheritdoc />
    public Task<Result<SetReport>> CopyToAccountAsync(string fromAccountId, IReadOnlyList<EmailCopyEntry> entries, CancellationToken ct) => own.CopyToAccountAsync(fromAccountId, entries, ct);

    // groups the ids by source, maps the folders each call names, sends each group to its own account and joins the reports
    private async Task<Result<SetReport>> RouteAsync(
        IReadOnlyList<string> emailIds,
        Func<IMailWriter, IReadOnlyList<string>, IReadOnlyDictionary<string, string?>?, CancellationToken, Task<Result<SetReport>>> send,
        IReadOnlyList<string>? mailboxes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        var groups = emailIds.GroupBy(id => UnifiedRows.ParseRow(id) is { } r ? r with { Id = "" } : null).ToList();
        var reports = new List<SetReport>();
        for (var i = 0; i < groups.Count; i++)
        {
            var report = await SendGroupAsync(groups[i].Key, [.. groups[i]], send, mailboxes, ct).ConfigureAwait(false);
            if (!report.IsOk)
            {
                // an earlier group (and the chunks of this one before the failure) are applied: the failure carries them (R3-03); the groups after it
                // are not sent
                var notSent = (report.Value?.NotSent ?? [.. groups[i]]).Concat(groups.Skip(i + 1).SelectMany(g => g)).ToList();
                reports.Add(report.Value ?? SetReport.Empty);
                return Result<SetReport>.FailPartial(report.Error!, Join(reports) with { NotSent = notSent });
            }

            reports.Add(report.Value!);
        }

        return Result<SetReport>.Ok(Join(reports));
    }

    private static SetReport Join(List<SetReport> reports) => new(
        [.. reports.SelectMany(r => r.Updated)],
        [.. reports.SelectMany(r => r.Destroyed)],
        [.. reports.SelectMany(r => r.Created)],
        [.. reports.SelectMany(r => r.Refused)]);

    private async Task<Result<SetReport>> SendGroupAsync(
        UnifiedRowId? source,
        List<string> ids,
        Func<IMailWriter, IReadOnlyList<string>, IReadOnlyDictionary<string, string?>?, CancellationToken, Task<Result<SetReport>>> send,
        IReadOnlyList<string>? mailboxes,
        CancellationToken ct)
    {
        if (source is null)
        {
            return await send(own, ids, mailboxes?.ToDictionary(m => m, m => (string?)m, StringComparer.Ordinal), ct).ConfigureAwait(false);
        }

        if (writerOf(source) is not { } writer)
        {
            return Refuse(ids, $"{source.Login} is not signed in.");
        }

        Dictionary<string, string?>? map = null;
        if (mailboxes is not null)
        {
            map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var m in mailboxes)
            {
                map[m] = await mapMailbox(source, m, ct).ConfigureAwait(false);
            }

            if (map[mailboxes[0]] is null)
            {
                return Refuse(ids, "That folder has no match in the message's account.");
            }
        }

        var real = ids.ToDictionary(UnifiedRows.RealId, id => id, StringComparer.Ordinal);
        var sent = await send(writer, [.. real.Keys], map, ct).ConfigureAwait(false);
        string Row(string id) => real.GetValueOrDefault(id) ?? id;
        SetReport Rows(SetReport r) => new(
            [.. r.Updated.Select(Row)],
            [.. r.Destroyed.Select(Row)],
            r.Created,
            [.. r.Refused.Select(e => e with { Id = Row(e.Id) })])
        {
            NotSent = [.. r.NotSent.Select(Row)],
        };

        // a failure keeps what its chunks did before it, under the row ids
        return sent.IsOk ? Result<SetReport>.Ok(Rows(sent.Value!)) : sent.Value is { } partial ? Result<SetReport>.FailPartial(sent.Error!, Rows(partial)) : sent;
    }

    private static Result<SetReport> Refuse(IEnumerable<string> ids, string why) =>
        Result<SetReport>.Ok(SetReport.Empty with { Refused = [.. ids.Select(id => new SetError(id, "forbidden", why, null))] });
}
