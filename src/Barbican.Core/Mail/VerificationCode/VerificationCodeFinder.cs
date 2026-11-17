// Mirrors: Rust otp.rs:282 find (subject capped at 1000 characters, best candidate of subject then text); Bulwark lib/verification-code.ts:275-346.
// Stub from stage F of M2; owner reader.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.VerificationCode;

/// <summary>The default finder: code words near a 4 to 8 character code, promo and order numbers skipped.</summary>
public sealed class VerificationCodeFinder : IVerificationCodeFinder
{
    /// <inheritdoc />
    public string? Find(string? subject, string? text)
    {
        throw new PortPendingException("otp.rs:282 find");
    }
}
