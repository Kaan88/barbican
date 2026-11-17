// New tests: the actions of the account popover. Bulwark components/layout/account-switcher.tsx:442-681 (Set as default for an account that is not the default, with two or more;
// Sign out of the account on screen; Sign out of all with two or more; drag and Alt+Shift+Up/Down order only a non-default account with three or more). spec I-007, I-008, I-013..I-015.
// Both sign-outs ask first (spec/menus.md ACCOUNT). Owner actions (m2-close-a).

#pragma warning disable MA0002, MA0006 // asserts compare keys ordinally by nature

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Accounts;

public sealed class AccountPopoverActionsTests : IDisposable
{
    private static readonly AccountInfo Ana = FakeAccounts.Info("ana@example.org");
    private static readonly AccountInfo Ben = FakeAccounts.Info("ben@example.net", "https://mail.example.net");
    private static readonly AccountInfo Cy = FakeAccounts.Info("cy@example.com", "https://mail.example.com");

    private readonly FakeAccounts _accounts = new();
    private readonly Maintenance _maintenance = new();
    private readonly Confirmer _confirmer = new();
    private readonly AccountsViewModel _vm;

    public AccountPopoverActionsTests() => _vm = new AccountsViewModel(_accounts, _accounts, new InlineDispatcher(), _maintenance, _confirmer);

    public void Dispose() => _vm.Dispose();

    [Fact]
    public void Set_as_default_shows_for_an_account_that_is_not_the_default_with_two_or_more()
    {
        _accounts.Set(new AccountStatus(Ana, false, true, true, 0, null), new AccountStatus(Ben, true, false, true, 0, null));
        Assert.True(_vm.CanSetDefault);
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        Assert.False(_vm.CanSetDefault);
        _accounts.Set(new AccountStatus(Ben, true, false, true, 0, null));
        Assert.False(_vm.CanSetDefault);
        Assert.False(_vm.CanSignOutAll);
        Assert.True(_vm.CanSignOut);
    }

    [Fact]
    public async Task Set_as_default_runs_on_the_account_on_screen()
    {
        _accounts.Set(new AccountStatus(Ana, false, true, true, 0, null), new AccountStatus(Ben, true, false, true, 0, null));
        await _vm.SetDefaultCommand.ExecuteAsync(null);
        Assert.Equal(["default:" + Ben.Key], _maintenance.Calls);
    }

    [Fact]
    public async Task Sign_out_asks_first_and_declining_signs_nothing_out()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        Assert.Equal("Sign out of ana@example.org", _vm.SignOutLabel);
        _confirmer.Answer = false;
        await _vm.SignOutCommand.ExecuteAsync(null);
        Assert.Empty(_maintenance.Calls);
        Assert.Equal("Sign out of ana@example.org?", Assert.Single(_confirmer.Asked).Title);

        _confirmer.Answer = true;
        await _vm.SignOutCommand.ExecuteAsync(null);
        Assert.Equal(["out:" + Ana.Key], _maintenance.Calls);
    }

    [Fact]
    public async Task Sign_out_of_all_asks_first_and_needs_two_accounts()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        Assert.True(_vm.CanSignOutAll);
        _confirmer.Answer = false;
        await _vm.SignOutAllCommand.ExecuteAsync(null);
        Assert.Empty(_maintenance.Calls);
        _confirmer.Answer = true;
        await _vm.SignOutAllCommand.ExecuteAsync(null);
        Assert.Equal(["all"], _maintenance.Calls);
    }

    [Fact]
    public async Task Alt_shift_arrows_move_only_a_non_default_account_and_only_with_three_accounts()
    {
        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null));
        await _vm.MoveAsync(_vm.Rows[1], -1);
        Assert.Empty(_maintenance.Calls);

        _accounts.Set(new AccountStatus(Ana, true, true, true, 0, null), new AccountStatus(Ben, false, false, true, 0, null), new AccountStatus(Cy, false, false, true, 0, null));
        await _vm.MoveAsync(_vm.Rows[0], 1);
        Assert.Empty(_maintenance.Calls);
        await _vm.MoveAsync(_vm.Rows[2], -1);
        Assert.Equal(["move:" + Cy.Key + ":1"], _maintenance.Calls);
        await _vm.MoveAsync(_vm.Rows[1], -1);
        Assert.Single(_maintenance.Calls);
    }

    private sealed class Maintenance : IAccountMaintenance
    {
        public List<string> Calls { get; } = [];

        public Task<Result<Unit>> SignOutAsync(string accountKey, CancellationToken ct) => RecordAsync("out:" + accountKey);

        public Task<Result<Unit>> SignOutAllAsync(CancellationToken ct) => RecordAsync("all");

        public Task<Result<Unit>> SetDefaultAsync(string accountKey, CancellationToken ct) => RecordAsync("default:" + accountKey);

        public Task<Result<Unit>> MoveAsync(string accountKey, int newIndex, CancellationToken ct) => RecordAsync($"move:{accountKey}:{newIndex}");

        private Task<Result<Unit>> RecordAsync(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    private sealed class Confirmer : IActionConfirmer
    {
        public bool Answer { get; set; }

        public List<ConfirmRequest> Asked { get; } = [];

        public Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
        {
            Asked.Add(request);
            return Task.FromResult(Answer);
        }
    }
}
