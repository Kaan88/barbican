// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>A fetched body part value (RFC 8621 §4.1.4 EmailBodyValue).</summary>
public sealed record BodyValue(string Value, bool IsTruncated = false, bool IsEncodingProblem = false);
