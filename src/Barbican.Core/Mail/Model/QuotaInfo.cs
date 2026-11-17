// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The storage quota that applies to the account (octets, RFC 9425; Rust jmap.rs:1157 pick_quota).</summary>
public sealed record QuotaInfo(long Used, long HardLimit);
