// Mirrors: Rust msgload.rs fetch :59 (tag rust-final): the body from the cache or the server (cid: images inlined, a prefetch leaves
// messages with large inline images to the open itself), the conversation from the cache or the server sorted by receivedAt; both land in
// the cache unless a clear happened meanwhile (generation guard, bodycache.rs put_gen). Bulwark components/email/email-viewer.tsx and
// lib/email-prefetch.ts (fetch once, cache, reuse). Stalwart crates/jmap/src/email/get.rs (bodyValues, maxBodyValueBytes, isTruncated).
// Owner body.
// Fable review 1 F-03: every HTML body is sanitized here (MailSanitizer), off the UI thread and once per fetch, before it is cached; the
// original of a changed part stays in CidBodyValues.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Fetches what the reader shows for a message, through the body cache.</summary>
public sealed class MessageFetcher(IBodyCache cache, IBlobSource blobs)
{
    /// <summary>The cached body and (when wanted) conversation, or null when either is missing.</summary>
    public FetchedMessage? Cached(string accountKey, string emailId, bool wantThread)
    {
        if (!cache.TryGet(accountKey, emailId, out var body))
        {
            return null;
        }

        if (!wantThread || body.ThreadId is null)
        {
            return new FetchedMessage(body, null);
        }

        return cache.TryGetThread(accountKey, body.ThreadId, out var thread) ? new FetchedMessage(body, thread) : null;
    }

    /// <summary>The cached body alone (the unread state of a sibling not in the list).</summary>
    public EmailBody? CachedBody(string accountKey, string emailId) => cache.TryGet(accountKey, emailId, out var body) ? body : null;

    /// <summary>Whether the body is cached (a prefetch skips it).</summary>
    public bool Contains(string accountKey, string emailId) => cache.TryGet(accountKey, emailId, out _);

    /// <summary>Fetches body and conversation; a failed conversation is no failure (the strip stays empty).</summary>
    public async Task<Result<FetchedMessage>> FetchAsync(string accountKey, IMailService mail, string emailId, bool wantThread, bool prefetch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var generation = cache.Generation;
        if (!cache.TryGet(accountKey, emailId, out var body))
        {
            var r = await mail.GetBodyAsync(emailId, new BodyFetchOptions { Prefetch = prefetch }, ct).ConfigureAwait(false);
            if (!r.IsOk || r.Value is null)
            {
                return Result<FetchedMessage>.Fail(r.Error ?? new TransportError("No message.", false));
            }

            body = BodyShape.Normalize(r.Value);
            if (prefetch && MessageLoadRules.InlineBytes(body) > MessageLoadRules.PrefetchInlineMax)
            {
                return Result<FetchedMessage>.Fail(new TransportError("large inline images", false));
            }

            body = await CidImages.InlineAsync(body, (part, token) => blobs.DownloadAsync(part, CidImages.MaxBytes, token), ct).ConfigureAwait(false);
            var inlined = body;
            body = await Task.Run(() => MailSanitizer.Apply(inlined), ct).ConfigureAwait(false);
            cache.Put(accountKey, body, generation);
        }

        ConversationThread? thread = null;
        if (wantThread && body.ThreadId is { } threadId && !cache.TryGetThread(accountKey, threadId, out thread))
        {
            var t = await mail.GetThreadAsync(threadId, ct).ConfigureAwait(false);
            if (t.IsOk && t.Value is { } fetched)
            {
                thread = fetched with { Emails = [.. fetched.Emails.OrderBy(e => e.ReceivedAt ?? DateTimeOffset.MinValue)] };
                cache.PutThread(accountKey, thread, generation);
            }
        }

        return Result<FetchedMessage>.Ok(new FetchedMessage(body, thread));
    }
}
