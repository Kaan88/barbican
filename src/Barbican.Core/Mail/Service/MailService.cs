// Mirrors: Rust jmap.rs:406 mailboxes_with_state (Mailbox/get ids:null; at maxObjectsInGet the state first, then Mailbox/query +
// chunked gets), :428 role_ids (cached 5 minutes), :524 list_page, :648 email_full, :666 thread_emails, :831 identities (the
// submission account), :1146 quota (Quota/get + pick_quota); Bulwark lib/jmap/client.ts:2635 getMailboxes, :3085 getEmails, :3517
// getEmail, :5073 getThread, :5177 getIdentities, :2518 getQuota; RFC 8621 §2, §4, §6; RFC 9425 §4 (Quota). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Quota;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Service;

/// <summary>The read side of mail over one <see cref="IJmapClient"/>.</summary>
public sealed class MailService(IJmapClient client, TimeProvider time) : IMailService
{
    private static readonly TimeSpan RoleTtl = TimeSpan.FromMinutes(5);
    private readonly Lock _roleLock = new();
    private (DateTimeOffset At, IReadOnlyDictionary<string, string> Roles)? _roles;
    private (IReadOnlyList<JsonObject> List, string? State)? _mailboxes;

    /// <summary>The client this service reads through.</summary>
    public IJmapClient Client => client;

    /// <summary>The folder objects last read (or brought up to date by Mailbox/changes) with their state; null before the first read.</summary>
    public (IReadOnlyList<JsonObject> List, string? State)? LastMailboxes
    {
        get
        {
            lock (_roleLock)
            {
                return _mailboxes;
            }
        }
    }

    /// <summary>Keeps a folder list merged from a delta, with its new state (Rust mail.rs:285).</summary>
    public void Remember(IReadOnlyList<JsonObject> list, string? state)
    {
        ArgumentNullException.ThrowIfNull(list);
        RememberRoles(list);
        lock (_roleLock)
        {
            _mailboxes = (list, state);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MailboxTree>> GetMailboxesAsync(CancellationToken ct)
    {
        var raw = await GetMailboxObjectsAsync(ct).ConfigureAwait(false);
        return raw.Map(r => Tree(r.List, r.State));
    }

    /// <summary>The folder tree as server objects with its Mailbox state (for Mailbox/changes).</summary>
    public async Task<Result<(IReadOnlyList<JsonObject> List, string? State)>> GetMailboxObjectsAsync(CancellationToken ct)
    {
        var acc = client.Session.MailAccountId;
        var props = JmapProperties.Json(JmapProperties.Mailbox);
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Mailbox/get", new JsonObject { ["accountId"] = acc, ["ids"] = null, ["properties"] = props }, "0")), ct)
            .ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<(IReadOnlyList<JsonObject>, string?)>.Fail(r.Error!);
        }

        var res = r.Value!.MethodResponses[0][1];
        IReadOnlyList<JsonObject> list = [.. res.Arr("list")?.OfType<JsonObject>() ?? []];
        var state = res.Str("state");
        if (list.Count >= client.Session.Limits.MaxObjectsInGet)
        {
            // the server may cut ids:null at maxObjectsInGet: the state first (a delta from it can only repeat, never miss), then pages
            var s = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Mailbox/get", new JsonObject { ["accountId"] = acc, ["ids"] = new JsonArray(), ["properties"] = new JsonArray("id") }, "s")), ct)
                .ConfigureAwait(false);
            var ids = s.IsOk ? await client.QueryAllAsync("Mailbox/query", acc, [], ct).ConfigureAwait(false) : Result<IReadOnlyList<string>>.Fail(s.Error!);
            var all = ids.IsOk ? await client.GetAllAsync("Mailbox/get", acc, ids.Value!, props, ct).ConfigureAwait(false) : Result<IReadOnlyList<JsonObject>>.Fail(ids.Error!);
            if (!all.IsOk)
            {
                return Result<(IReadOnlyList<JsonObject>, string?)>.Fail(all.Error!);
            }

            state = s.Value!.MethodResponses[0][1].Str("state");
            list = all.Value!;
        }

        Remember(list, state);
        return Result<(IReadOnlyList<JsonObject>, string?)>.Ok((list, state));
    }

    /// <summary>The tree of server objects.</summary>
    public static MailboxTree Tree(IReadOnlyList<JsonObject> list, string? state) =>
        new([.. list.Select(ModelReader.Mailbox).OfType<Mailbox>()]) { State = state };

    /// <summary>Role → mailbox id (inbox, drafts, sent, ...), cached for five minutes.</summary>
    public async Task<Result<IReadOnlyDictionary<string, string>>> GetRoleIdsAsync(CancellationToken ct)
    {
        lock (_roleLock)
        {
            if (_roles is { } r && time.GetUtcNow() - r.At < RoleTtl)
            {
                return Result<IReadOnlyDictionary<string, string>>.Ok(r.Roles);
            }
        }

        var list = await GetMailboxObjectsAsync(ct).ConfigureAwait(false);
        return list.Map(_ => _roles!.Value.Roles);
    }

    /// <summary>Keeps the role map of a freshly read tree.</summary>
    public void RememberRoles(IReadOnlyList<JsonObject> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in list)
        {
            if (m.Str("role") is { } role && m.Str("id") is { } id)
            {
                roles[role] = id;
            }
        }

        lock (_roleLock)
        {
            _roles = (time.GetUtcNow(), roles);
        }
    }

    /// <inheritdoc/>
    public Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ListPageFetcher.FetchAsync(client, ListFilters.For(query), after, query.PageSize, ct);
    }

    /// <inheritdoc/>
    public async Task<Result<EmailBody>> GetBodyAsync(string emailId, BodyFetchOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var args = new JsonObject
        {
            ["accountId"] = client.Session.MailAccountId,
            ["ids"] = new JsonArray(emailId),
            ["properties"] = JmapProperties.Json(JmapProperties.EmailFull),
            ["bodyProperties"] = JmapProperties.Json(JmapProperties.Body),
            ["fetchTextBodyValues"] = true,
            ["fetchHTMLBodyValues"] = true,
            ["maxBodyValueBytes"] = options.MaxBodyValueBytes ?? JmapProperties.MaxBodyValueBytes,
        };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Email/get", args, "g")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<EmailBody>.Fail(r.Error!);
        }

        var body = ModelReader.Body(r.Value!.MethodResponses[0][1].Arr("list") is { Count: > 0 } l ? l[0] : null);
        return body is null ? Result<EmailBody>.Fail(new MethodError("Email/get", "notFound", "Message not found.")) : Result<EmailBody>.Ok(body);
    }

    /// <inheritdoc/>
    public async Task<Result<ConversationThread>> GetThreadAsync(string threadId, CancellationToken ct)
    {
        var acc = client.Session.MailAccountId;
        var b = new JmapRequestBuilder();
        var t = b.Add("Thread/get", new JsonObject { ["accountId"] = acc, ["ids"] = new JsonArray(threadId) });
        b.Add("Email/get", new JsonObject { ["accountId"] = acc, ["#ids"] = t.Ref("/list/*/emailIds"), ["properties"] = JmapProperties.Json(JmapProperties.EmailList) });
        var r = await client.CallAsync(b.Build(), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<ConversationThread>.Fail(r.Error!);
        }

        var list = r.Value!.MethodResponses[1][1].Arr("list") ?? [];
        var rows = list.Select(ModelReader.Summary).OfType<EmailSummary>().Select(e => e with { ThreadCount = list.Count }).ToList();
        return Result<ConversationThread>.Ok(new ConversationThread(threadId, rows));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Identity>>> GetIdentitiesAsync(CancellationToken ct)
    {
        var acc = client.Session.AccountFor(JmapCapabilities.Submission);
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Identity/get", new JsonObject { ["accountId"] = acc, ["ids"] = null }, "i")), ct)
            .ConfigureAwait(false);
        return r.Map(v => (IReadOnlyList<Identity>)[.. (v.MethodResponses[0][1].Arr("list") ?? []).Select(ModelReader.Identity).OfType<Identity>()]);
    }

    /// <inheritdoc/>
    public async Task<Result<QuotaInfo?>> GetQuotaAsync(CancellationToken ct)
    {
        if (!client.Session.Has(JmapCapabilities.Quota))
        {
            return Result<QuotaInfo?>.Ok(null);
        }

        var acc = client.Session.AccountFor(JmapCapabilities.Quota);
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Quota/get", new JsonObject { ["accountId"] = acc, ["ids"] = null }, "q")), ct)
            .ConfigureAwait(false);
        return r.Map(v =>
        {
            var q = QuotaPicker.Pick([.. (v.MethodResponses[0][1].Arr("list") ?? []).OfType<JsonObject>()]);
            return q is null ? null : new QuotaInfo((long)q.Number("used"), (long)q.Number("hardLimit"));
        });
    }
}
