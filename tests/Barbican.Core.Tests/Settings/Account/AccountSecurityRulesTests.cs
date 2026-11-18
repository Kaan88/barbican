// Not Rust rows: G-027, G-028, I-403 (Bulwark account-security-settings.tsx form checks). M6 stream set-data.

using Barbican.Core.Settings.Account;

namespace Barbican.Core.Tests.Settings.Account;

public sealed class AccountSecurityRulesTests
{
    [Fact]
    public void A_password_change_is_checked_before_it_is_sent()
    {
        Assert.Null(AccountSecurityRules.PasswordProblem("old-password", "new-password-1", "new-password-1"));
        Assert.NotNull(AccountSecurityRules.PasswordProblem(string.Empty, "new-password-1", "new-password-1"));
        Assert.NotNull(AccountSecurityRules.PasswordProblem("old-password", "short", "short"));
        Assert.NotNull(AccountSecurityRules.PasswordProblem("old-password", "old-password", "old-password"));
        Assert.NotNull(AccountSecurityRules.PasswordProblem("old-password", "new-password-1", "new-password-2"));
    }

    [Fact]
    public void A_totp_code_is_six_digits_with_spaces_ignored()
    {
        Assert.True(AccountSecurityRules.IsTotpCode("123456"));
        Assert.True(AccountSecurityRules.IsTotpCode("123 456"));
        Assert.False(AccountSecurityRules.IsTotpCode("12345"));
        Assert.False(AccountSecurityRules.IsTotpCode("12345a"));
    }
}
