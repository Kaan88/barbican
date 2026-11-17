// Mirrors: Rust msgload.rs fetch :59 (tag rust-final): the body from the cache or the server (cid: images inlined, a prefetch leaves
// messages with large inline images to the open itself), the conversation from the cache or the server sorted by receivedAt; both land in
// the cache unless a clear happened meanwhile (generation guard, bodycache.rs put_gen). Bulwark components/email/email-viewer.tsx and
// lib/email-prefetch.ts (fetch once, cache, reuse). Stalwart crates/jmap/src/email/get.rs (bodyValues, maxBodyValueBytes, isTruncated).
// Owner body.
// Fable review 1 F-03: every HTML body is sanitized here (MailSanitizer), off the UI thread and once per fetch, before it is cached; the
// original of a changed part stays in CidBodyValues.
// Fable review 3 R3-01: a list id is first resolved to the account that holds it (IReadRoutes: a unified row of another login or of a shared
// account goes to that account, with its server id); the cache keeps server ids under that account's key, and what comes out carries the row
// ids again (body, thread and its members), so the reader, its conversation strip and the actions keep speaking in list ids.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Fetches what the reader shows for a message, through the body cache.</summary>
/// <param name="cache">The body cache (server ids per account key).</param>
/// <param name="blobs">The inline-image source of the account on screen.</param>
/// <param name="routes">Resolves list ids of other accounts (the unified inbox); null when every id is the account on screen's.</param>
public sealed class MessageFetcher(IBodyCache cache, IBlobSource blobs, IReadRoutes? routes = null)
{
    /// <summary>The cached body and (when wanted) conversation, or null when either is missing.</summary>
    public FetchedMessage? Cached(string accountKey, string emailId, bool wantThread)
    {
        var (key, id) = CacheOf(accountKey, emailId);
        if (!cache.TryGet(key, id, out var body))
        {
            return null;
        }

        if (!wantThread || body.ThreadId is null)
        {
            return Tagged(emailId, new FetchedMessage(body, null));
        }

        return cache.TryGetThread(key, body.ThreadId, out var thread) ? Tagged(emailId, new FetchedMessage(body, thread)) : null;
    }

    /// <summary>The cached body alone (the unread state of a sibling not in the list).</summary>
    public EmailBody? CachedBody(string accountKey, string emailId)
    {
        var (key, id) = CacheOf(accountKey, emailId);
        return cache.TryGet(key, id, out var body) ? Tagged(emailId, body) : null;
    }

    /// <summary>Whether the body is cached (a prefetch skips it).</summary>
    public bool Contains(string accountKey, string emailId)
    {
        var (key, id) = CacheOf(accountKey, emailId);
        return cache.TryGet(key, id, out _);
    }

    /// <summary>The account that holds a list row: its login (server, pinned trace id), its source key (the login, plus <c>#account</c> for a
    /// shared one: the cache and the folder roles) and its read service; the account on screen for a plain id or when the row's login is not
    /// connected.</summary>
    public (string Login, string SourceKey, IMailService Mail) OwnerOf(string accountKey, IMailService mail, string emailId)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var (key, _) = CacheOf(accountKey, emailId);
        return routes?.Route(accountKey, mail, emailId) is { IsOk: true, Value: { } route } && UnifiedRows.ParseRow(emailId) is { } row
            ? (row.Login, key, route.Mail)
            : (accountKey, accountKey, mail);
    }

    /// <summary>Fetches body and conversation; a failed conversation is no failure (the strip stays empty).</summary>
    public async Task<Result<FetchedMessage>> FetchAsync(string accountKey, IMailService mail, string emailId, bool wantThread, bool prefetch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var routed = routes?.Route(accountKey, mail, emailId);
        if (routed is { IsOk: false } refused)
        {
            return Result<FetchedMessage>.Fail(refused.Error!);
        }

        var (key, id) = CacheOf(accountKey, emailId);
        var (service, source) = routed?.Value is { } route ? (route.Mail, route.Blobs ?? blobs) : (mail, blobs);
        var generation = cache.Generation;
        if (!cache.TryGet(key, id, out var body))
        {
            var r = await service.GetBodyAsync(id, new BodyFetchOptions { Prefetch = prefetch }, ct).ConfigureAwait(false);
            if (!r.IsOk || r.Value is null)
            {
                return Result<FetchedMessage>.Fail(r.Error ?? new TransportError("No message.", false));
            }

            body = BodyShape.Normalize(r.Value);
            if (prefetch && MessageLoadRules.InlineBytes(body) > MessageLoadRules.PrefetchInlineMax)
            {
                return Result<FetchedMessage>.Fail(new TransportError("large inline images", false));
            }

            body = await CidImages.InlineAsync(body, (part, token) => source.DownloadAsync(part, CidImages.MaxBytes, token), ct).ConfigureAwait(false);
            var inlined = body;
            body = await Task.Run(() => MailSanitizer.Apply(inlined), ct).ConfigureAwait(false);
            cache.Put(key, body, generation);
        }

        ConversationThread? thread = null;
        if (wantThread && body.ThreadId is { } threadId && !cache.TryGetThread(key, threadId, out thread))
        {
            var t = await service.GetThreadAsync(threadId, ct).ConfigureAwait(false);
            if (t.IsOk && t.Value is { } fetched)
            {
                thread = fetched with { Emails = [.. fetched.Emails.OrderBy(e => e.ReceivedAt ?? DateTimeOffset.MinValue)] };
                cache.PutThread(key, thread, generation);
            }
        }

        return Result<FetchedMessage>.Ok(Tagged(emailId, new FetchedMessage(body, thread)));
    }

    // the cache key and server id of a list id: a unified row's own account (the login, plus "#account" for a shared one) and its server id
    // (no connection needed, so a cache hit works while that login reconnects); a plain id is the account on screen's
    private static (string Key, string Id) CacheOf(string accountKey, string rowId) =>
        UnifiedRows.ParseRow(rowId) is { } r ? (UnifiedRows.SourceKey(r.Login, r.SharedAccount), r.Id) : (accountKey, rowId);

    // what the cache and the server hold carries server ids; the reader gets the row's own ids back (a plain id stays as it is)
    private static FetchedMessage Tagged(string rowId, FetchedMessage m)
    {
        if (UnifiedRows.ParseRow(rowId) is null)
        {
            return m;
        }

        var thread = m.Thread is { } t
            ? t with { ThreadId = UnifiedRows.SameSource(rowId, t.ThreadId), Emails = [.. t.Emails.Select(e => e with { Id = UnifiedRows.SameSource(rowId, e.Id), ThreadId = e.ThreadId is { } x ? UnifiedRows.SameSource(rowId, x) : null })] }
            : null;
        return new FetchedMessage(Tagged(rowId, m.Body), thread);
    }

    private static EmailBody Tagged(string rowId, EmailBody body) =>
        UnifiedRows.ParseRow(rowId) is null ? body : body with { Id = rowId, ThreadId = body.ThreadId is { } t ? UnifiedRows.SameSource(rowId, t) : null };
}
