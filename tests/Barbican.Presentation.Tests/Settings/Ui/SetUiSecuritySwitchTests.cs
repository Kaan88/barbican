// Regression: delayed security results must not surface after switching accounts, including a switch back.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Settings.Account;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Ui;
using Barbican.Presentation.Tests.Accounts;

#pragma warning disable VSTHRD003 // The test deliberately controls completion after an account switch.

namespace Barbican.Presentation.Tests.Settings.Ui;

public sealed class SetUiSecuritySwitchTests
{
    [Fact]
    public async Task Late_secret_and_quota_are_discarded_even_after_switching_back()
    {
        var a = FakeAccounts.Info("a@example.org");
        var b = FakeAccounts.Info("b@example.org");
        var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(a, true, true, true, null, null), new AccountStatus(b, false, false, true, null, null));
        var screen = new SetUiViewModel(new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation(), accounts, new InlineDispatcher());
        var security = new DelayedSecurity();
        var quota = new DelayedQuota();
        var vm = new SetUiSecurityViewModel(security, quota, screen, new InlineDispatcher());
        var load = vm.RefreshAsync(TestContext.Current.CancellationToken);
        var issue = vm.CreateAppPasswordAsync("test", TestContext.Current.CancellationToken);
        await accounts.SwitchToAsync(b.Key, TestContext.Current.CancellationToken);
        await accounts.SwitchToAsync(a.Key, TestContext.Current.CancellationToken);
        quota.Answer.SetResult(Result<AccountQuotaUsage>.Ok(new AccountQuotaUsage(42, 100)));
        security.Answer.SetResult(Result<AccountIssuedSecret>.Ok(new AccountIssuedSecret("id", "secret")));
        await Task.WhenAll(load, issue);
        Assert.Equal(string.Empty, vm.IssuedSecret);
        Assert.Equal(string.Empty, vm.QuotaText);
        Assert.Empty(vm.AppPasswords);
    }

    private sealed class DelayedQuota : IAccountQuota
    {
        public TaskCompletionSource<Result<AccountQuotaUsage>> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Result<AccountQuotaUsage>> UsageAsync(string accountKey, CancellationToken ct) => Answer.Task;
    }

    private sealed class DelayedSecurity : IAccountSecurity
    {
        private readonly SampleAccountSecurity _inner = new();
        public TaskCompletionSource<Result<AccountIssuedSecret>> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable(string key) => _inner.IsAvailable(key);
        public Task<Result<Unit>> ChangePasswordAsync(string k, string a, string b, CancellationToken c) => _inner.ChangePasswordAsync(k, a, b, c);
        public Task<Result<AccountTotp>> TotpAsync(string k, CancellationToken c) => _inner.TotpAsync(k, c);
        public Task<Result<AccountTotp>> BeginTotpAsync(string k, string p, CancellationToken c) => _inner.BeginTotpAsync(k, p, c);
        public Task<Result<Unit>> ConfirmTotpAsync(string k, string code, CancellationToken c) => _inner.ConfirmTotpAsync(k, code, c);
        public Task<Result<Unit>> DisableTotpAsync(string k, string p, CancellationToken c) => _inner.DisableTotpAsync(k, p, c);
        public Task<Result<IReadOnlyList<AccountAppPassword>>> AppPasswordsAsync(string k, CancellationToken c) => _inner.AppPasswordsAsync(k, c);
        public Task<Result<AccountIssuedSecret>> CreateAppPasswordAsync(string k, string n, DateTimeOffset? e, CancellationToken c) => Answer.Task;
        public Task<Result<Unit>> DeleteAppPasswordAsync(string k, string id, CancellationToken c) => _inner.DeleteAppPasswordAsync(k, id, c);
        public Task<Result<IReadOnlyList<AccountApiKey>>> ApiKeysAsync(string k, CancellationToken c) => _inner.ApiKeysAsync(k, c);
        public Task<Result<AccountIssuedSecret>> CreateApiKeyAsync(string k, string d, DateTimeOffset? e, CancellationToken c) => _inner.CreateApiKeyAsync(k, d, e, c);
        public Task<Result<Unit>> DeleteApiKeyAsync(string k, string id, CancellationToken c) => _inner.DeleteApiKeyAsync(k, id, c);
        public Task<Result<string?>> DisplayNameAsync(string k, CancellationToken c) => _inner.DisplayNameAsync(k, c);
        public Task<Result<Unit>> SetDisplayNameAsync(string k, string n, CancellationToken c) => _inner.SetDisplayNameAsync(k, n, c);
        public Task<Result<AccountSecurityEncryption>> EncryptionAsync(string k, CancellationToken c) => _inner.EncryptionAsync(k, c);
        public Task<Result<Unit>> SetEncryptionAsync(string k, bool e, string? p, CancellationToken c) => _inner.SetEncryptionAsync(k, e, p, c);
    }
}
