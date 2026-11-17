// Mirrors: Rust bodycache.rs:21 `type Key = (String, String)` (account state key, list id).

namespace Barbican.Core.Mail.Cache;

/// <summary>A cache entry's key: the account and the listed id (message or thread).</summary>
internal readonly record struct CacheKey(string Account, string Id);
