// Mirrors: Bulwark account-security-settings.tsx (the password form checks); Stalwart's own checks stay the server's. Contract from the M6 plan section 4.5; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The pure rules of the security page.</summary>
public static class AccountSecurityRules
{
    /// <summary>What is wrong with a password change before it is sent, or null: the current password is required, the new one must be at least 8 characters, different from the current one, and equal to its confirmation.</summary>
    public static string? PasswordProblem(string current, string next, string confirm) => throw new PortPendingException("AccountSecurityRules.PasswordProblem");

    /// <summary>Whether a typed TOTP code is acceptable to send: six digits, spaces ignored.</summary>
    public static bool IsTotpCode(string code) => throw new PortPendingException("AccountSecurityRules.IsTotpCode");
}
