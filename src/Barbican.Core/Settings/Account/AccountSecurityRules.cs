// Mirrors: Bulwark account-security-settings.tsx (the password form checks); Stalwart's own checks stay the server's. Contract from the M6 plan section 4.5; owner set-data.

namespace Barbican.Core.Settings.Account;

/// <summary>The pure rules of the security page.</summary>
public static class AccountSecurityRules
{
    /// <summary>The shortest new password the form sends.</summary>
    public const int MinPasswordLength = 8;

    /// <summary>What is wrong with a password change before it is sent, or null: the current password is required, the new one must be at least 8 characters, different from the current one, and equal to its confirmation.</summary>
    public static string? PasswordProblem(string current, string next, string confirm)
    {
        if (string.IsNullOrEmpty(current))
        {
            return "Enter your current password.";
        }

        if (next is null || next.Length < MinPasswordLength)
        {
            return "The new password must be at least 8 characters long.";
        }

        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return "The new password must be different from the current one.";
        }

        return string.Equals(next, confirm, StringComparison.Ordinal) ? null : "The two new passwords are not the same.";
    }

    /// <summary>Whether a typed TOTP code is acceptable to send: six digits, spaces ignored.</summary>
    public static bool IsTotpCode(string code)
    {
        if (code is null)
        {
            return false;
        }

        var digits = code.Replace(" ", string.Empty, StringComparison.Ordinal);
        return digits.Length == 6 && digits.All(char.IsAsciiDigit);
    }
}
