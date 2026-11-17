// Mirrors: Rust bodycache.rs (whole file: BodyCache :25-:215, constants :17-:20); M1-plan §4.2. Stub from F2; owner accounts.
// Keyed by account and the listed message id, bounded by entries and bytes, least recently used out first.

using Barbican.Core.Common;
using System.Diagnostics.CodeAnalysis;
using Barbican.Core.Mail.Model;

// CA1822: instance members of a stub that no stream has implemented yet; each gains state when its stream lands.
#pragma warning disable CA1822

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

    public BodyCache(int maxEntries = DefaultMaxEntries, long maxBytes = DefaultMaxBytes)
    {
        _ = maxEntries;
        _ = maxBytes;
    }

    public long Generation => throw new PortPendingException("BodyCache.Generation: accounts stream");

    /// <summary>Bodies held.</summary>
    public int Count => throw new PortPendingException("BodyCache.Count: accounts stream");

    /// <summary>Estimated bytes of the bodies held.</summary>
    public long Bytes => throw new PortPendingException("BodyCache.Bytes: accounts stream");

    /// <summary>Estimated bytes of the conversations held.</summary>
    public long ThreadBytes => throw new PortPendingException("BodyCache.ThreadBytes: accounts stream");

    /// <summary>Rough size of a message in memory: its body values dominate, then headers and parts (Rust <c>weight</c>).</summary>
    public static long Weight(EmailBody body) => throw new PortPendingException("BodyCache.Weight: accounts stream");

    /// <summary>Rough size of a conversation's rows (Rust <c>thread_weight</c>).</summary>
    public static long ThreadWeight(ConversationThread thread) => throw new PortPendingException("BodyCache.ThreadWeight: accounts stream");

    public bool TryGet(string accountKey, string emailId, [NotNullWhen(true)] out EmailBody? body) =>
        throw new PortPendingException("BodyCache.TryGet: accounts stream");

    /// <summary>True when the entry is held (does not count as a use).</summary>
    public bool Contains(string accountKey, string listId) => throw new PortPendingException("BodyCache.Contains: accounts stream");

    /// <summary>Stores a body under a list id (which may differ from the message id: unified inbox rows), evicting the least recently used.</summary>
    public EmailBody Put(string accountKey, string listId, EmailBody body) => throw new PortPendingException("BodyCache.Put: accounts stream");

    public void Put(string accountKey, EmailBody body, long fetchedAtGeneration) =>
        throw new PortPendingException("BodyCache.Put: accounts stream");

    /// <summary>Stores under a list id unless a clear happened since <paramref name="generation"/>; returns the body either way.</summary>
    public EmailBody Put(string accountKey, string listId, EmailBody body, long generation) =>
        throw new PortPendingException("BodyCache.Put: accounts stream");

    public bool TryGetThread(string accountKey, string threadId, [NotNullWhen(true)] out ConversationThread? thread) =>
        throw new PortPendingException("BodyCache.TryGetThread: accounts stream");

    public ConversationThread PutThread(string accountKey, string threadId, ConversationThread thread) =>
        throw new PortPendingException("BodyCache.PutThread: accounts stream");

    public void PutThread(string accountKey, ConversationThread thread, long fetchedAtGeneration) =>
        throw new PortPendingException("BodyCache.PutThread: accounts stream");

    /// <summary>Stores a conversation unless a clear happened since <paramref name="generation"/>; returns it either way.</summary>
    public ConversationThread PutThread(string accountKey, string threadId, ConversationThread thread, long generation) =>
        throw new PortPendingException("BodyCache.PutThread: accounts stream");

    /// <summary>A sync brought a fresh copy of a listed message: take over its keywords and mailboxes.</summary>
    public void Patch(string accountKey, string listId, EmailSummary fresh) => throw new PortPendingException("BodyCache.Patch: accounts stream");

    public void Remove(string accountKey, string listId) => throw new PortPendingException("BodyCache.Remove: accounts stream");

    public void DropThread(string accountKey, string threadId) => throw new PortPendingException("BodyCache.DropThread: accounts stream");

    /// <summary>
    /// A local action changed messages (read, flag, tag): cached bodies and the rows of cached conversations take the change, in whichever
    /// account holds them. <paramref name="ids"/> are list ids; <paramref name="real"/> maps one to the server's id.
    /// </summary>
    public void PatchLocal(IReadOnlyList<string> ids, Func<string, string> real, Func<EmailBody, EmailBody> patchBody, Func<EmailSummary, EmailSummary> patchRow) =>
        throw new PortPendingException("BodyCache.PatchLocal: accounts stream");

    /// <summary>Messages left the list by a local action: their bodies go, and so do the conversations they were in.</summary>
    public void RemoveLocal(IReadOnlyList<string> ids, Func<string, string> real) => throw new PortPendingException("BodyCache.RemoveLocal: accounts stream");

    /// <summary>Applies a list sync: fresh copies, ids that left the list, threads that changed.</summary>
    public void OnSync(string accountKey, IReadOnlyDictionary<string, EmailSummary> fresh, IReadOnlyList<string> removed, IReadOnlyList<string> threads) =>
        throw new PortPendingException("BodyCache.OnSync: accounts stream");

    public void ApplySync(string accountKey, MailSyncEvent change) => throw new PortPendingException("BodyCache.ApplySync: accounts stream");

    public void Clear() => throw new PortPendingException("BodyCache.Clear: accounts stream");
}
