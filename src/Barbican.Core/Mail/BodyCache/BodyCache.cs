// Mirrors: Rust bodycache.rs (whole file at rust-final: BodyCache :23-:215, constants :16-:19, thread_weight :40, weight :45, put_gen :72,
// put :98, patch :114, patch_local :159, remove_local :175, on_sync :187, clear :196); M1-plan §4.2, §6.3; spec B-SYNC-012..021.
// Keyed by account and the listed message id, bounded by entries and bytes, least recently used out first. A body never changes in
// JMAP (RFC 8621 §4: only keywords and mailboxIds are mutable), so syncs patch those two and drop what left. Thread-safe: the loader,
// prefetch and the sync hub call it from different threads (Rust used one process-wide Mutex, bodycache.rs:203).

using System.Diagnostics.CodeAnalysis;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Cache;

/// <summary>The in-memory body and conversation cache.</summary>
public sealed class BodyCache : IBodyCache
{
    /// <summary>Default entry bound of the body cache.</summary>
    public const int DefaultMaxEntries = 120;

    /// <summary>Default byte bound of the body cache (48 MiB).</summary>
    public const long DefaultMaxBytes = 48L * 1024 * 1024;

    /// <summary>Entry bound of the conversation cache.</summary>
    public const int MaxThreads = 120;

    /// <summary>Byte bound of the conversation cache (8 MiB).</summary>
    public const long MaxThreadBytes = 8L * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly LruMap<EmailBody> _bodies = new();
    private readonly LruMap<ConversationThread> _threads = new();
    private long _generation;

    public BodyCache(int maxEntries = DefaultMaxEntries, long maxBytes = DefaultMaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    /// <inheritdoc/>
    public long Generation
    {
        get
        {
            lock (_lock)
            {
                return _generation;
            }
        }
    }

    /// <summary>Bodies held.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _bodies.Count;
            }
        }
    }

    /// <summary>Estimated bytes of the bodies held.</summary>
    public long Bytes
    {
        get
        {
            lock (_lock)
            {
                return _bodies.Bytes;
            }
        }
    }

    /// <summary>Estimated bytes of the conversations held.</summary>
    public long ThreadBytes
    {
        get
        {
            lock (_lock)
            {
                return _threads.Bytes;
            }
        }
    }

    /// <summary>Rough size of a message in memory: its body values dominate, then headers and parts (Rust <c>weight</c>, bodycache.rs:45).</summary>
    public static long Weight(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        long n = 512;
        foreach (var h in body.Headers)
        {
            n += 48 + h.Name.Length + h.Value.Length;
        }

        n += 256L * (body.Attachments.Count + body.HtmlBody.Count + body.TextBody.Count + body.To.Count + body.Cc.Count + body.Bcc.Count + body.From.Count + body.ReplyTo.Count);
        n += (body.Preview?.Length ?? 0) + (body.Subject?.Length ?? 0);
        foreach (var v in body.BodyValues.Values)
        {
            n += v.Value.Length;
        }

        foreach (var v in body.CidBodyValues.Values)
        {
            n += v.Length;
        }

        return n;
    }

    /// <summary>Rough size of a conversation's rows (Rust <c>thread_weight</c>, bodycache.rs:40).</summary>
    public static long ThreadWeight(ConversationThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        long n = 0;
        foreach (var r in thread.Emails)
        {
            n += 640 + (r.Preview?.Length ?? 0) + (r.Subject?.Length ?? 0);
        }

        return n;
    }

    /// <inheritdoc/>
    public bool TryGet(string accountKey, string emailId, [NotNullWhen(true)] out EmailBody? body)
    {
        lock (_lock)
        {
            return _bodies.TryGet(Key(accountKey, emailId), out body);
        }
    }

    /// <summary>True when the entry is held (does not count as a use).</summary>
    public bool Contains(string accountKey, string listId)
    {
        lock (_lock)
        {
            return _bodies.Contains(Key(accountKey, listId));
        }
    }

    /// <summary>Stores a body under a list id (which may differ from the message id: unified inbox rows), evicting the least recently used.</summary>
    public EmailBody Put(string accountKey, string listId, EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_lock)
        {
            _bodies.Put(Key(accountKey, listId), body, Weight(body), _maxEntries, _maxBytes);
            return body;
        }
    }

    /// <inheritdoc/>
    public void Put(string accountKey, EmailBody body, long fetchedAtGeneration)
    {
        ArgumentNullException.ThrowIfNull(body);
        Put(accountKey, body.Id, body, fetchedAtGeneration);
    }

    /// <summary>Stores under a list id unless a clear happened since <paramref name="generation"/>; returns the body either way.</summary>
    public EmailBody Put(string accountKey, string listId, EmailBody body, long generation)
    {
        ArgumentNullException.ThrowIfNull(body);
        lock (_lock)
        {
            if (generation == _generation)
            {
                _bodies.Put(Key(accountKey, listId), body, Weight(body), _maxEntries, _maxBytes);
            }

            return body;
        }
    }

    /// <inheritdoc/>
    public bool TryGetThread(string accountKey, string threadId, [NotNullWhen(true)] out ConversationThread? thread)
    {
        lock (_lock)
        {
            return _threads.TryGet(Key(accountKey, threadId), out thread);
        }
    }

    /// <summary>Stores a conversation, evicting the least recently used.</summary>
    public ConversationThread PutThread(string accountKey, string threadId, ConversationThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        lock (_lock)
        {
            _threads.Put(Key(accountKey, threadId), thread, ThreadWeight(thread), MaxThreads, MaxThreadBytes);
            return thread;
        }
    }

    /// <inheritdoc/>
    public void PutThread(string accountKey, ConversationThread thread, long fetchedAtGeneration)
    {
        ArgumentNullException.ThrowIfNull(thread);
        PutThread(accountKey, thread.ThreadId, thread, fetchedAtGeneration);
    }

    /// <summary>Stores a conversation unless a clear happened since <paramref name="generation"/>; returns it either way.</summary>
    public ConversationThread PutThread(string accountKey, string threadId, ConversationThread thread, long generation)
    {
        ArgumentNullException.ThrowIfNull(thread);
        lock (_lock)
        {
            if (generation == _generation)
            {
                _threads.Put(Key(accountKey, threadId), thread, ThreadWeight(thread), MaxThreads, MaxThreadBytes);
            }

            return thread;
        }
    }

    /// <summary>A sync brought a fresh copy of a listed message: take over its keywords and mailboxes (Rust <c>patch</c>, bodycache.rs:114).</summary>
    public void Patch(string accountKey, string listId, EmailSummary fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        lock (_lock)
        {
            PatchLocked(Key(accountKey, listId), fresh);
        }
    }

    /// <summary>Drops one body.</summary>
    public void Remove(string accountKey, string listId)
    {
        lock (_lock)
        {
            _bodies.Remove(Key(accountKey, listId));
        }
    }

    /// <summary>Drops one conversation.</summary>
    public void DropThread(string accountKey, string threadId)
    {
        lock (_lock)
        {
            _threads.Remove(Key(accountKey, threadId));
        }
    }

    /// <summary>
    /// A local action changed messages (read, flag, tag): cached bodies and the rows of cached conversations take the change, in whichever
    /// account holds them. <paramref name="ids"/> are list ids; <paramref name="real"/> maps one to the server's id.
    /// </summary>
    public void PatchLocal(IReadOnlyList<string> ids, Func<string, string> real, Func<EmailBody, EmailBody> patchBody, Func<EmailSummary, EmailSummary> patchRow)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(patchBody);
        ArgumentNullException.ThrowIfNull(patchRow);
        var listIds = new HashSet<string>(ids, StringComparer.Ordinal);
        var realIds = new HashSet<string>(ids.Select(real), StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var key in _bodies.Keys.Where(k => listIds.Contains(k.Id)).ToList())
            {
                _bodies.Update(key, patchBody);
            }

            foreach (var key in _threads.Keys.ToList())
            {
                _threads.Update(key, t => t.Emails.Any(r => realIds.Contains(r.Id))
                    ? t with { Emails = [.. t.Emails.Select(r => realIds.Contains(r.Id) ? patchRow(r) : r)] }
                    : t);
            }
        }
    }

    /// <summary>Messages left the list by a local action: their bodies go, and so do the conversations they were in.</summary>
    public void RemoveLocal(IReadOnlyList<string> ids, Func<string, string> real)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(real);
        var listIds = new HashSet<string>(ids, StringComparer.Ordinal);
        var realIds = new HashSet<string>(ids.Select(real), StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var key in _bodies.Keys.Where(k => listIds.Contains(k.Id)).ToList())
            {
                _bodies.Remove(key);
            }

            foreach (var key in _threads.Keys.ToList())
            {
                if (_threads.Peek(key) is { } t && t.Emails.Any(r => realIds.Contains(r.Id)))
                {
                    _threads.Remove(key);
                }
            }
        }
    }

    /// <summary>Applies a list sync: fresh copies, ids that left the list, threads that changed (Rust <c>on_sync</c>, bodycache.rs:187).</summary>
    public void OnSync(string accountKey, IReadOnlyDictionary<string, EmailSummary> fresh, IReadOnlyList<string> removed, IReadOnlyList<string> threads)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(threads);
        lock (_lock)
        {
            foreach (var (id, m) in fresh)
            {
                PatchLocked(Key(accountKey, id), m);
                if (m.ThreadId is { } t)
                {
                    _threads.Remove(Key(accountKey, t));
                }
            }

            foreach (var id in removed)
            {
                _bodies.Remove(Key(accountKey, id));
            }

            foreach (var t in threads)
            {
                _threads.Remove(Key(accountKey, t));
            }
        }
    }

    /// <inheritdoc/>
    public void ApplySync(string accountKey, MailSyncEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        switch (change)
        {
            case EmailsChangedEvent e:
                OnSync(accountKey, e.Fresh, e.Removed, e.Threads);
                break;
            case ListDeltaEvent d:
                // the list's view of the same delta (jmap also emits EmailsChangedEvent for it); applying both is idempotent
                OnSync(accountKey, d.Delta.Emails, [], d.Delta.DestroyedThreads);
                break;
            default:
                // mailbox counts, resets and push state do not touch bodies: a reset re-reads the list, and every body it shows is still valid
                break;
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_lock)
        {
            _bodies.Clear();
            _threads.Clear();
            _generation++;
        }
    }

    private static CacheKey Key(string accountKey, string id)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(id);
        return new CacheKey(accountKey, id);
    }

    private void PatchLocked(CacheKey key, EmailSummary fresh)
    {
        // Rust copies keywords and mailboxIds when the fresh copy has them; a message is always in at least one mailbox, so an empty map
        // means the property was not fetched
        _bodies.Update(key, m => m with
        {
            Keywords = fresh.Keywords,
            MailboxIds = fresh.MailboxIds.Count > 0 ? fresh.MailboxIds : m.MailboxIds,
        });
    }
}
