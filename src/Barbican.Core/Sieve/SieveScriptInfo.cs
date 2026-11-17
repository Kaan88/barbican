// Mirrors: RFC 9661 section 2 (SieveScript: id, name, blobId, isActive); Stalwart crates/jmap/src/sieve/get.rs; Rust quick_rules.rs:229 save (properties id, name, blobId, isActive).

namespace Barbican.Core.Sieve;

/// <summary>One Sieve script of the account.</summary>
public sealed record SieveScriptInfo(string Id, string Name, string BlobId, bool IsActive);
