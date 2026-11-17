// Mirrors: Rust bodycache.rs (module comment :1-:12); M1-plan §4.2. Contract stub from F2; owner accounts.

using System.Diagnostics.CodeAnalysis;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Cache;

/// <summary>In-memory cache of opened and prefetched message bodies and conversations.</summary>
public interface IBodyCache
{
    /// <summary>Changes whenever the cache is cleared (sign-out); fetches started before store nothing.</summary>
    long Generation { get; }

    bool TryGet(string accountKey, string emailId, [NotNullWhen(true)] out EmailBody? body);

    /// <summary>Stores the body, unless a clear happened since <paramref name="fetchedAtGeneration"/>.</summary>
    void Put(string accountKey, EmailBody body, long fetchedAtGeneration);

    bool TryGetThread(string accountKey, string threadId, [NotNullWhen(true)] out ConversationThread? thread);

    void PutThread(string accountKey, ConversationThread thread, long fetchedAtGeneration);

    /// <summary>Patches keywords and mailboxes, drops removed messages and changed threads.</summary>
    void ApplySync(string accountKey, MailSyncEvent change);

    void Clear();
}
