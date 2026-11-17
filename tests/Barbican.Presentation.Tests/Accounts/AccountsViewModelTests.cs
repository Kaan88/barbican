// Bulwark components/layout/account-switcher.tsx (avatar, tooltip, other accounts' unread dot, rows with check, default star, unread badge,
// server line, Add account) and Rust main.rs on_switch_account (a failed switch keeps the account and says why). spec I-001, I-002, I-005,
// I-006, I-010.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Accounts;

public sealed class AccountsViewModelTests : IDisposable
{
    private static readonly AccountInfo Ana = FakeAccounts.Info("ana@example.org");
    private static readonly AccountInfo Ben = FakeAccounts.Info("ben@example.net", "https://mail.example.net");
    private static readonly AccountInfo Ana2 = FakeAccounts.Info("ana@example.org", "https://other.example.com:8443");

    private readonly FakeAccounts _accounts = new();
    private readonly AccountsViewModel _vm;

    public AccountsViewModelTests() => _vm = new AccountsViewModel(_accounts, _accounts, new InlineDispatcher());

    public void Dispose() => _vm.Dispose();

    [Fact]
    public void The_avatar_shows_the_active_account_and_the_others_unread()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 5, null), new AccountStatus(Ben, false, false, true, 3, null));
        Assert.Equal("ana@example.org", _vm.ActiveTitle);
        Assert.Equal("A", _vm.ActiveInitial);
        Assert.Equal(3, _vm.OthersUnread);
        Assert.True(_vm.HasOthersUnread);
        Assert.Equal("ana@example.org\njmap.example.org\n3 unread in other accounts", _vm.Tooltip);
    }

    [Fact]
    public void Rows_show_check_star_badge_and_server_only_when_needed()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 120, null), new AccountStatus(Ben, false, false, false, null, "Could not connect."));
        var ana = _vm.Rows[0];
        var ben = _vm.Rows[1];
        Assert.True(ana.IsActive);
        Assert.True(ana.ShowStar);
        Assert.Equal("99+", ana.UnreadText);
        Assert.Null(ana.ServerLine);
        Assert.False(ben.ShowStar);
        Assert.Null(ben.UnreadText);
        Assert.True(ben.HasError);
        Assert.Equal("mail.example.net", ben.ServerLine);
        Assert.Equal(0, _vm.OthersUnread);
    }

    [Fact]
    public void One_account_has_no_star_and_two_with_one_address_show_their_servers()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null));
        Assert.False(_vm.Rows[0].ShowStar);
        Assert.Null(_vm.Rows[0].UnreadText);

        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ana2, false, false, true, 0, null));
        Assert.Equal("jmap.example.org", _vm.Rows[0].ServerLine);
        Assert.Equal("other.example.com:8443", _vm.Rows[1].ServerLine);
    }

    [Fact]
    public void Rows_are_updated_in_place()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 1, null), new AccountStatus(Ben, false, false, true, 1, null));
        var ana = _vm.Rows[0];
        var ben = _vm.Rows[1];
        _accounts.Set(new AccountStatus(Ben, true, false, true, 0, null), new AccountStatus(Ana, false, true, true, 9, null));
        Assert.Same(ben, _vm.Rows[0]);
        Assert.Same(ana, _vm.Rows[1]);
        Assert.Equal("9", ana.UnreadText);

        _accounts.Set(new AccountStatus(Ben, true, false, true, 0, null));
        Assert.Single(_vm.Rows);
        Assert.Same(ben, _vm.Rows[0]);
    }

    [Fact]
    public async Task A_row_click_switches_and_closes()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        _vm.ToggleCommand.Execute(null);
        Assert.True(_vm.IsOpen);
        await _vm.SwitchCommand.ExecuteAsync(_vm.Rows[1]);
        Assert.False(_vm.IsOpen);
        Assert.Equal("ben@example.net", _vm.ActiveTitle);
        Assert.Null(_vm.Message);
    }

    [Fact]
    public async Task A_failed_switch_keeps_the_account_and_says_why()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        _accounts.SwitchAnswer = Result<Unit>.Fail(new AuthError("Could not switch to ben@example.net: offline", NeedsSignIn: false));
        await _vm.SwitchCommand.ExecuteAsync(_vm.Rows[1]);
        Assert.Equal("ana@example.org", _vm.ActiveTitle);
        Assert.Equal("Could not switch to ben@example.net: offline", _vm.Message);
        Assert.False(_vm.IsSwitching);
    }

    [Fact]
    public void Add_account_closes_and_asks_the_shell()
    {
        var asked = 0;
        _vm.AddAccountRequested += (_, _) => asked++;
        _vm.IsOpen = true;
        _vm.AddAccountCommand.Execute(null);
        Assert.Equal(1, asked);
        Assert.False(_vm.IsOpen);
        Assert.Equal("Sign in to another account. Your other accounts stay signed in.", AccountsViewModel.AddAccountStatus);
    }
}
