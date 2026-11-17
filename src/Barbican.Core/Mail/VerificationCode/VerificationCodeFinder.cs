// Mirrors: Rust otp.rs (a dependency-free port of Bulwark lib/verification-code.ts:1-346): :39 is_uppercase_code_word, :45 is_code_word_part,
// :64 normalize, :88 find_code_words, :120 labels_short_number, :135 candidates, :167 window, :175 last_word, :180 labelled_as_other,
// :190 has_promo_words, :198 score, :262 best_candidate, :282 find (subject capped at 1000 characters, best candidate of subject then text).
// A token counts only when it looks like a code and sits near a word that names one; doubtful tokens are left out (a wrong chip is worse than none).
// Text is handled as Unicode scalars (Rune), as the Rust char vector.

using System.Text;
using Barbican.Core.Mail.VerificationCode.Internal;

namespace Barbican.Core.Mail.VerificationCode;

/// <summary>The default finder: code words near a 4 to 8 character code, promo and order numbers skipped.</summary>
public sealed class VerificationCodeFinder : IVerificationCodeFinder
{
    /// <inheritdoc />
    public string? Find(string? subject, string? text)
    {
        var s = subject ?? string.Empty;
        var capped = new StringBuilder();
        var n = 0;
        foreach (var r in s.EnumerateRunes())
        {
            if (n++ >= 1000)
            {
                break;
            }

            capped.Append(r.ToString());
        }

        return CodeScan.BestCandidate(capped.ToString()) ?? CodeScan.BestCandidate(text ?? string.Empty);
    }
}
