// Mirrors: M2 plan section 4.5 (IVerificationCodeFinder); Rust otp.rs:282 find; Bulwark lib/verification-code.ts:275. Stub contract from stage F of M2; owner reader.

namespace Barbican.Core.Mail.VerificationCode;

/// <summary>Finds a one-time code in a message (the list chip, the reader chip, Copy code).</summary>
public interface IVerificationCodeFinder
{
    /// <summary>The code of a mail, or null; a code in the subject wins over the text.</summary>
    string? Find(string? subject, string? text);
}
