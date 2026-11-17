// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>Where the next page starts (RFC 8620 §5.5 anchor paging: the last id shown).</summary>
public sealed record ListCursor(string AnchorId);
