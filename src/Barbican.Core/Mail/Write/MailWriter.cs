// Mirrors: RFC 8621 §4.6 (Email/set), §4.7 (Email/copy), §4.8 (Email/import), §2.5 (Mailbox/set); RFC 8620 §5.3 (patches are JSON Pointers),
// §5.5 (Email/query with result references), §6.1 (upload). Bulwark lib/jmap/client.ts batchUpdateKeywords :3859, batchDeleteEmails :4090,
// batchMoveEmails :4134, removeEmailsFromMailbox :4360, emptyMailbox :4389, markMailboxAsRead :4474, createMailbox :4636, updateMailboxes :4702,
// deleteMailbox :4729, importEmail :6770, copyEmailAcrossAccounts :12278. Stalwart crates/jmap/src/{email/{set,copy,import},mailbox/set}.rs.
// Rules (M1 transport): every call is chunked by the server's maxObjectsInSet, a write is never retried, a read-only account is refused here
// without sending a request, and every notUpdated / notDestroyed is a SetError whose Message is the user text (SetErrors.Message).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.KeywordSyntax;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Write;

/// <summary>The <see cref="IMailWriter"/> of one account over one JMAP client.</summary>
public sealed class MailWriter(IJmapClient client, string accountId) : IMailWriter
{
    private const int PageLimit = 500;
    private const string SeenKeyword = "$seen";

    /// <inheritdoc />
    public Task<Result<SetReport>> UpdateKeywordsAsync(IReadOnlyList<string> emailIds, IReadOnlyDictionary<string, bool> patch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        ArgumentNullException.ThrowIfNull(patch);
        if (emailIds.Count == 0 || patch.Count == 0)
        {
            return Task.FromResult(Result<SetReport>.Ok(SetReport.Empty));
        }

        foreach (var keyword in patch.Keys)
        {
            if (!KeywordRules.IsValid(keyword))
            {
                return Task.FromResult(Result<SetReport>.Fail(new ValidationError($"\"{keyword}\" is not a valid tag.")));
            }
        }

        return UpdateEmailsAsync(emailIds, () =>
        {
            var o = new JsonObject();
            foreach (var (keyword, on) in patch)
            {
                o[KeywordRules.Path(keyword)] = on ? true : null;
            }

            return o;
        }, ct);
    }

    /// <inheritdoc />
    public Task<Result<SetReport>> MoveAsync(IReadOnlyList<string> emailIds, string? fromMailboxId, string toMailboxId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        ArgumentException.ThrowIfNullOrEmpty(toMailboxId);
        if (emailIds.Count == 0)
        {
            return Task.FromResult(Result<SetReport>.Ok(SetReport.Empty));
        }

        // From a known folder: a pointer patch, so any other folder the message is in stays (labels). Without one: the whole set
        // is replaced by the target, as Bulwark's batchMoveEmails does.
        return UpdateEmailsAsync(emailIds, () =>
        {
            if (fromMailboxId is null || string.Equals(fromMailboxId, toMailboxId, StringComparison.Ordinal))
            {
                return new JsonObject { ["mailboxIds"] = new JsonObject { [toMailboxId] = true } };
            }

            return new JsonObject
            {
                ["mailboxIds/" + KeywordRules.PointerToken(fromMailboxId)] = null,
                ["mailboxIds/" + KeywordRules.PointerToken(toMailboxId)] = true,
            };
        }, ct);
    }

    /// <inheritdoc />
    public Task<Result<SetReport>> MoveWithKeywordsAsync(IReadOnlyList<string> emailIds, string toMailboxId, IReadOnlyDictionary<string, bool> keywords, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentException.ThrowIfNullOrEmpty(toMailboxId);
        return UpdateEmailsAsync(emailIds, () =>
        {
            var o = new JsonObject { ["mailboxIds"] = new JsonObject { [toMailboxId] = true } };
            foreach (var (keyword, on) in keywords)
            {
                o[KeywordRules.Path(keyword)] = on ? true : null;
            }

            return o;
        }, ct);
    }

    /// <inheritdoc />
    public Task<Result<SetReport>> RemoveFromMailboxAsync(IReadOnlyList<string> emailIds, string mailboxId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        ArgumentException.ThrowIfNullOrEmpty(mailboxId);
        return UpdateEmailsAsync(emailIds, () => new JsonObject { ["mailboxIds/" + KeywordRules.PointerToken(mailboxId)] = null }, ct);
    }

    /// <inheritdoc />
    public Task<Result<SetReport>> SetMailboxIdsAsync(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> mailboxIdsById, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mailboxIdsById);
        var items = mailboxIdsById
            .Select(kv => (kv.Key, (JsonNode)new JsonObject { ["mailboxIds"] = new JsonObject(kv.Value.Select(m => KeyValuePair.Create(m.Key, (JsonNode?)(m.Value ? true : null)))) }))
            .ToList();
        return SetChunksAsync("Email/set", "update", items, ct);
    }

    /// <inheritdoc />
    public async Task<Result<SetReport>> DestroyAsync(IReadOnlyList<string> emailIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        if (emailIds.Count == 0)
        {
            return Result<SetReport>.Ok(SetReport.Empty);
        }

        if (Refused("Email/set") is { } refusal)
        {
            return refusal;
        }

        var report = SetReport.Empty;
        var done = 0;
        foreach (var chunk in emailIds.Chunk(MaxObjectsInSet))
        {
            var args = new JsonObject { ["accountId"] = accountId, ["destroy"] = new JsonArray(chunk.Select(id => (JsonNode?)id).ToArray()) };
            var r = await SetOnceAsync("Email/set", args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                // the chunks before this one are applied on the server: say so (R3-03), the caller restores only what was not applied
                var rest = emailIds.Skip(done).ToList();
                return Result<SetReport>.FailPartial(r.Error!, report with { NotSent = rest });
            }

            report = Merge(report, r.Value!);
            done += chunk.Length;
        }

        return Result<SetReport>.Ok(report);
    }

    /// <inheritdoc />
    public async Task<Result<int>> EmptyMailboxAsync(string mailboxId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(mailboxId);
        if (Refused("Email/set") is { } refusal)
        {
            return Result<int>.Fail(refusal.Error!);
        }

        var batch = Math.Min(PageLimit, MaxObjectsInSet);
        var total = 0;

        // Never gate the loop on the query's total (Bulwark :4389): destroy a page until a short or empty one comes back.
        while (true)
        {
            var page = await EmptyPageAsync(mailboxId, batch, ct).ConfigureAwait(false);
            if (!page.IsOk)
            {
                return Result<int>.Fail(page.Error!);
            }

            total += page.Value.Destroyed;

            // everything in this page refused: say so instead of reporting the folder as emptied
            if (page.Value.Found > 0 && page.Value.Destroyed == 0)
            {
                return Result<int>.Fail(page.Value.FirstError is { } fe ? (JmapError)fe : new ValidationError("Failed to empty the folder."));
            }

            if (page.Value.Found < batch)
            {
                return Result<int>.Ok(total);
            }
        }
    }

    private async Task<Result<(int Found, int Destroyed, SetError? FirstError)>> EmptyPageAsync(string mailboxId, int batch, CancellationToken ct)
    {
        var request = JmapRequest.Of(
            JmapRequest.Call("Email/query", new JsonObject { ["accountId"] = accountId, ["filter"] = new JsonObject { ["inMailbox"] = mailboxId }, ["limit"] = batch }, "0"),
            JmapRequest.Call(
                "Email/set",
                new JsonObject
                {
                    ["accountId"] = accountId,
                    ["#destroy"] = new JsonObject { ["resultOf"] = "0", ["name"] = "Email/query", ["path"] = "/ids" },
                },
                "1"));
        var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsOk)
        {
            return Result<(int, int, SetError?)>.Fail(response.Error!);
        }

        var query = response.Value!.Args("0", "Email/query");
        var set = response.Value!.Args("1", "Email/set");
        if (!query.IsOk || !set.IsOk)
        {
            return Result<(int, int, SetError?)>.Fail((query.IsOk ? set.Error : query.Error)!);
        }

        var outcome = SetErrors.Read(set.Value);
        return Result<(int, int, SetError?)>.Ok((query.Value!.Strings("ids").Count, outcome.Destroyed.Count, outcome.Failed.Count > 0 ? outcome.Failed[0] : null));
    }

    /// <inheritdoc />
    public async Task<Result<int>> MarkMailboxReadAsync(string mailboxId, bool withChildren, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(mailboxId);
        if (Refused("Email/set") is { } refusal)
        {
            return Result<int>.Fail(refusal.Error!);
        }

        var boxes = new List<string> { mailboxId };
        if (withChildren)
        {
            var tree = await DescendantsAsync(mailboxId, ct).ConfigureAwait(false);
            if (!tree.IsOk)
            {
                return Result<int>.Fail(tree.Error!);
            }

            boxes.AddRange(tree.Value!);
        }

        var page = Math.Min(PageLimit, MaxObjectsInSet);
        var total = 0;
        foreach (var box in boxes)
        {
            var n = await MarkOneAsync(box, page, ct).ConfigureAwait(false);
            if (!n.IsOk)
            {
                return n;
            }

            total += n.Value;
        }

        return Result<int>.Ok(total);
    }

    private async Task<Result<int>> MarkOneAsync(string box, int page, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var filter = new JsonObject
            {
                ["operator"] = "AND",
                ["conditions"] = new JsonArray(new JsonObject { ["inMailbox"] = box }, new JsonObject { ["notKeyword"] = SeenKeyword }),
            };
            var q = await client.SendAsync(
                JmapRequest.Of(JmapRequest.Call("Email/query", new JsonObject { ["accountId"] = accountId, ["filter"] = filter, ["limit"] = page }, "0")), ct).ConfigureAwait(false);
            var args = q.IsOk ? q.Value!.Args("0", "Email/query") : Result<JsonObject>.Fail(q.Error!);
            if (!args.IsOk)
            {
                return Result<int>.Fail(args.Error!);
            }

            var ids = args.Value!.Strings("ids");
            if (ids.Count == 0)
            {
                return Result<int>.Ok(total);
            }

            var set = await UpdateKeywordsAsync(ids, new Dictionary<string, bool>(StringComparer.Ordinal) { [SeenKeyword] = true }, ct).ConfigureAwait(false);
            if (!set.IsOk)
            {
                return Result<int>.Fail(set.Error!);
            }

            // a refused update leaves the ids unread and the next query would return them forever
            if (!set.Value!.AllAccepted)
            {
                return Result<int>.Fail(set.Value.Refused[0]);
            }

            total += ids.Count;
            if (ids.Count < page)
            {
                return Result<int>.Ok(total);
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<Mailbox>> CreateMailboxAsync(string name, string? parentId, string? role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Refused("Mailbox/set") is { } refusal)
        {
            return Result<Mailbox>.Fail(refusal.Error!);
        }

        // subscribed explicitly: IMAP clients listing with LSUB hide unsubscribed folders (Bulwark :4636)
        var create = new JsonObject { ["name"] = name, ["isSubscribed"] = true };
        if (!string.IsNullOrEmpty(parentId))
        {
            create["parentId"] = parentId;
        }

        if (!string.IsNullOrEmpty(role))
        {
            create["role"] = role;
        }

        var args = new JsonObject { ["accountId"] = accountId, ["create"] = new JsonObject { ["new-0"] = create } };
        var r = await SetOnceAsync("Mailbox/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Mailbox>.Fail(r.Error!);
        }

        if (r.Value!.Refused.Count > 0)
        {
            return Result<Mailbox>.Fail(r.Value.Refused[0]);
        }

        if (r.Value.Created.Count == 0)
        {
            return Result<Mailbox>.Fail(new ValidationError("The server did not return the new folder."));
        }

        return Result<Mailbox>.Ok(new Mailbox
        {
            Id = r.Value.Created[0],
            Name = name,
            ParentId = string.IsNullOrEmpty(parentId) ? null : parentId,
            Role = MailboxRoleConverter.Parse(role),
            IsSubscribed = true,
        });
    }

    /// <inheritdoc />
    public Task<Result<SetReport>> UpdateMailboxesAsync(IReadOnlyDictionary<string, MailboxPatch> patches, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patches);
        var items = patches.Select(kv => (kv.Key, (JsonNode)ToJson(kv.Value))).ToList();
        return SetChunksAsync("Mailbox/set", "update", items, ct);
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> DestroyMailboxAsync(string mailboxId, bool removeEmails, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(mailboxId);
        if (Refused("Mailbox/set") is { } refusal)
        {
            return Result<Unit>.Fail(refusal.Error!);
        }

        // the server refuses a non-empty folder (mailboxHasEmail) unless onDestroyRemoveEmails is set (RFC 8621 §2.5)
        var args = new JsonObject { ["accountId"] = accountId, ["destroy"] = new JsonArray(mailboxId) };
        if (removeEmails)
        {
            args["onDestroyRemoveEmails"] = true;
        }

        var r = await SetOnceAsync("Mailbox/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return r.Value!.Refused.Count > 0 ? Result<Unit>.Fail(r.Value.Refused[0]) : Result<Unit>.Ok(default);
    }

    /// <inheritdoc />
    public async Task<Result<UploadedBlob>> UploadAsync(ReadOnlyMemory<byte> data, string type, CancellationToken ct)
    {
        if (Refused("Blob/upload") is { } refusal)
        {
            return Result<UploadedBlob>.Fail(refusal.Error!);
        }

        if (client is not JmapClient http)
        {
            return Result<UploadedBlob>.Fail(new TransportError("This connection cannot upload.", Retryable: false));
        }

        var r = await http.UploadAsync(accountId, data, type, ct).ConfigureAwait(false);
        return r.IsOk ? Result<UploadedBlob>.Ok(new UploadedBlob(r.Value!.BlobId, r.Value.Type, r.Value.Size)) : Result<UploadedBlob>.Fail(r.Error!);
    }

    /// <inheritdoc />
    public async Task<Result<EmailImportReport>> ImportAsync(IReadOnlyList<EmailImportEntry> entries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (Refused("Email/import") is { } refusal)
        {
            return Result<EmailImportReport>.Fail(refusal.Error!);
        }

        var created = new Dictionary<int, string>();
        var refused = new List<SetError>();
        var offset = 0;
        foreach (var chunk in entries.Chunk(MaxObjectsInSet))
        {
            var emails = new JsonObject();
            for (var i = 0; i < chunk.Length; i++)
            {
                var e = chunk[i];
                var o = new JsonObject
                {
                    ["blobId"] = e.BlobId,
                    ["mailboxIds"] = new JsonObject(e.MailboxIds.Select(m => KeyValuePair.Create(m, (JsonNode?)true))),
                    ["keywords"] = new JsonObject(e.Keywords.Select(k => KeyValuePair.Create(k, (JsonNode?)true))),
                };
                if (e.ReceivedAt is not null)
                {
                    o["receivedAt"] = e.ReceivedAt;
                }

                emails[Key(offset + i)] = o;
            }

            var response = await client.SendAsync(
                JmapRequest.Of(JmapRequest.Call("Email/import", new JsonObject { ["accountId"] = accountId, ["emails"] = emails }, "0")), ct).ConfigureAwait(false);
            var args = response.IsOk ? response.Value!.Args("0", "Email/import") : Result<JsonObject>.Fail(response.Error!);
            if (!args.IsOk)
            {
                return Result<EmailImportReport>.Fail(args.Error!);
            }

            var outcome = SetErrors.Read(args.Value);
            foreach (var (cid, id) in outcome.Created)
            {
                created[int.Parse(cid[1..], System.Globalization.CultureInfo.InvariantCulture)] = id;
            }

            refused.AddRange(outcome.Failed);
            offset += chunk.Length;
        }

        return Result<EmailImportReport>.Ok(new EmailImportReport(created, refused));
    }

    /// <inheritdoc />
    public async Task<Result<SetReport>> CopyToAccountAsync(string fromAccountId, IReadOnlyList<EmailCopyEntry> entries, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromAccountId);
        ArgumentNullException.ThrowIfNull(entries);
        if (Refused("Email/copy") is { } refusal)
        {
            return refusal;
        }

        var report = SetReport.Empty;
        var offset = 0;
        foreach (var chunk in entries.Chunk(MaxObjectsInSet))
        {
            var create = new JsonObject();
            for (var i = 0; i < chunk.Length; i++)
            {
                var e = chunk[i];
                create[Key(offset + i)] = new JsonObject
                {
                    ["id"] = e.EmailId,
                    ["mailboxIds"] = new JsonObject(e.MailboxIds.Select(m => KeyValuePair.Create(m, (JsonNode?)true))),
                    ["keywords"] = new JsonObject(e.Keywords.Select(k => KeyValuePair.Create(k, (JsonNode?)true))),
                };
            }

            var args = new JsonObject { ["fromAccountId"] = fromAccountId, ["accountId"] = accountId, ["create"] = create };
            var r = await SetOnceAsync("Email/copy", args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<SetReport>.Fail(r.Error!);
            }

            report = Merge(report, r.Value!);
            offset += chunk.Length;
        }

        return Result<SetReport>.Ok(report);
    }

    private static string Key(int i) => "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static JsonObject ToJson(MailboxPatch p)
    {
        var o = new JsonObject();
        if (p.Name is not null)
        {
            o["name"] = p.Name;
        }

        if (p.SetParent)
        {
            o["parentId"] = p.ParentId;
        }

        if (p.SortOrder is { } so)
        {
            o["sortOrder"] = so;
        }

        if (p.IsSubscribed is { } sub)
        {
            o["isSubscribed"] = sub;
        }

        return o;
    }

    private static SetReport Merge(SetReport a, SetReport b) =>
        new([.. a.Updated, .. b.Updated], [.. a.Destroyed, .. b.Destroyed], [.. a.Created, .. b.Created], [.. a.Refused, .. b.Refused]);

    private int MaxObjectsInSet => Math.Max(1, client.Session.Limits.MaxObjectsInSet);

    /// <summary>Refuses a write on a read-only account before any request is built (the client refuses too; this one says it in words).</summary>
    private Result<SetReport>? Refused(string method) =>
        client.IsReadOnly ? Result<SetReport>.Fail(new ReadOnlyViolation(method) { Message = "This account is read-only." }) : null;

    private Task<Result<SetReport>> UpdateEmailsAsync(IReadOnlyList<string> ids, Func<JsonObject> patch, CancellationToken ct) =>
        SetChunksAsync("Email/set", "update", [.. ids.Select(id => (id, (JsonNode)patch()))], ct);

    private async Task<Result<SetReport>> SetChunksAsync(string method, string key, List<(string Id, JsonNode Change)> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return Result<SetReport>.Ok(SetReport.Empty);
        }

        if (Refused(method) is { } refusal)
        {
            return refusal;
        }

        var report = SetReport.Empty;
        var done = 0;
        foreach (var chunk in items.Chunk(MaxObjectsInSet))
        {
            var map = new JsonObject();
            foreach (var (id, change) in chunk)
            {
                map[id] = change.DeepClone();
            }

            var r = await SetOnceAsync(method, new JsonObject { ["accountId"] = accountId, [key] = map }, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                // a failed request stops the rest: nothing is retried, the later chunks stay unsent (Bulwark :4134 keeps going; a user
                // action that fails once should not fire more writes). The chunks before it are applied: they come back with the error (R3-03).
                var rest = items.Skip(done).Select(i => i.Id).ToList();
                return Result<SetReport>.FailPartial(r.Error!, report with { NotSent = rest });
            }

            report = Merge(report, r.Value!);
            done += chunk.Length;
        }

        return Result<SetReport>.Ok(report);
    }

    private async Task<Result<SetReport>> SetOnceAsync(string method, JsonObject args, CancellationToken ct)
    {
        var response = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "0")), ct).ConfigureAwait(false);
        if (!response.IsOk)
        {
            return Result<SetReport>.Fail(response.Error!);
        }

        var result = response.Value!.Args("0", method);
        if (!result.IsOk)
        {
            return Result<SetReport>.Fail(result.Error!);
        }

        var o = SetErrors.Read(result.Value);
        return Result<SetReport>.Ok(new SetReport(o.Updated, o.Destroyed, [.. o.Created.Values], o.Failed));
    }

    private async Task<Result<IReadOnlyList<string>>> DescendantsAsync(string mailboxId, CancellationToken ct)
    {
        var r = await client.SendAsync(
            JmapRequest.Of(JmapRequest.Call("Mailbox/get", new JsonObject { ["accountId"] = accountId, ["properties"] = new JsonArray("parentId") }, "0")), ct).ConfigureAwait(false);
        var args = r.IsOk ? r.Value!.Args("0", "Mailbox/get") : Result<JsonObject>.Fail(r.Error!);
        if (!args.IsOk)
        {
            return Result<IReadOnlyList<string>>.Fail(args.Error!);
        }

        var parents = (args.Value!.Arr("list") ?? []).OfType<JsonObject>().ToDictionary(m => m.Str("id") ?? "", m => m.Str("parentId"), StringComparer.Ordinal);
        var result = new List<string>();
        var frontier = new Queue<string>([mailboxId]);
        while (frontier.Count > 0)
        {
            var p = frontier.Dequeue();
            foreach (var (id, parent) in parents)
            {
                if (string.Equals(parent, p, StringComparison.Ordinal) && !result.Contains(id, StringComparer.Ordinal))
                {
                    result.Add(id);
                    frontier.Enqueue(id);
                }
            }
        }

        return Result<IReadOnlyList<string>>.Ok(result);
    }
}
