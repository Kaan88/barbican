// Mirrors: Bulwark lib/jmap/client.ts:2165 connect (Stalwart answers the session with 402 titled "TOTP code required" or, from
// 0.16, "MFA code required": ask for the one-time code); Rust jmap.rs:125 connect (`password$code`). Owner jmap.

namespace Barbican.Core.Jmap.Errors;

/// <summary>The password was right but the account needs a one-time code (sign in again with it).</summary>
public sealed record OtpRequiredError(string Message) : JmapError(Message);
