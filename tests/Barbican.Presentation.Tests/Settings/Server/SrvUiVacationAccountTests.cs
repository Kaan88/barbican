using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Settings.Vacation;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Tests.Accounts;

#pragma warning disable VSTHRD003 // Delayed completions model account switches during service calls.

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiVacationAccountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_is_refused_until_the_new_account_form_loads()
    {
        var (accounts, a, b) = Accounts();
        var service = new ControlledVacationService();
        var first = service.QueueLoad();
        var vm = new SrvUiVacationViewModel(service, accounts, new InlineDispatcher());
        var loading = vm.LoadAsync(Ct);
        first.SetResult(Result<VacationSettings>.Ok(Value("Account A")));
        await loading;
        vm.Body = "Unsaved A";
        var second = service.QueueLoad();
        await accounts.SwitchToAsync(b.Key, Ct);
        var loadingB = vm.LoadAsync(Ct);
        await vm.SaveAsync(Ct);
        Assert.Empty(service.Saves);
        Assert.Contains("load", vm.Error, StringComparison.OrdinalIgnoreCase);
        second.SetResult(Result<VacationSettings>.Ok(Value("Account B")));
        await loadingB;
        Assert.True(SpinWait.SpinUntil(() => string.Equals(vm.Body, "Account B", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain("Unsaved A", vm.Body, StringComparison.Ordinal);
        Assert.Equal(a.Key, service.Loads[0]);
        Assert.Equal(b.Key, service.Loads[1]);
    }

    [Fact]
    public async Task Late_save_and_load_are_discarded_after_a_b_a_switch()
    {
        var (accounts, a, b) = Accounts();
        var service = new ControlledVacationService();
        var first = service.QueueLoad();
        var vm = new SrvUiVacationViewModel(service, accounts, new InlineDispatcher());
        var loading = vm.LoadAsync(Ct);
        first.SetResult(Result<VacationSettings>.Ok(Value("Original A")));
        await loading;
        vm.Body = "Old A write";
        var saving = vm.SaveAsync(Ct);
        Assert.Equal("vacation-state", service.LastSaved!.State);
        Assert.Equal("sieve-state", service.LastSaved.SieveState);
        var bLoad = service.QueueLoad();
        await accounts.SwitchToAsync(b.Key, Ct);
        var loadingB = vm.LoadAsync(Ct);
        var aLoad = service.QueueLoad();
        await accounts.SwitchToAsync(a.Key, Ct);
        var loadingA = vm.LoadAsync(Ct);
        aLoad.SetResult(Result<VacationSettings>.Ok(Value("Fresh A")));
        await loadingA;
        Assert.True(SpinWait.SpinUntil(() => string.Equals(vm.Body, "Fresh A", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));
        bLoad.SetResult(Result<VacationSettings>.Ok(Value("Late B")));
        service.SaveResult.SetResult(Result<VacationSettings>.Ok(Value("Late A save")));
        await Task.WhenAll(saving, loadingB);
        Assert.Equal("Fresh A", vm.Body);
        Assert.False(string.Equals("Saved.", vm.Message, StringComparison.Ordinal));
        Assert.Equal(a.Key, Assert.Single(service.Saves));
    }

    private static VacationSettings Value(string text) => new(false, "", text, null, null, null, null, null, false) { State = "vacation-state", SieveState = "sieve-state" };

    private static (FakeAccounts Accounts, AccountInfo A, AccountInfo B) Accounts()
    {
        var a = FakeAccounts.Info("a@example.org");
        var b = FakeAccounts.Info("b@example.org");
        var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(a, true, true, true, null, null), new AccountStatus(b, false, false, true, null, null));
        return (accounts, a, b);
    }

    private sealed class ControlledVacationService : IVacationService
    {
        private readonly Queue<TaskCompletionSource<Result<VacationSettings>>> _loads = new();
        public List<string> Loads { get; } = [];
        public List<string> Saves { get; } = [];
        public VacationSettings? LastSaved { get; private set; }
        public TaskCompletionSource<Result<VacationSettings>> SaveResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<VacationChangedEventArgs>? Changed { add { } remove { } }

        public TaskCompletionSource<Result<VacationSettings>> QueueLoad()
        {
            var completion = new TaskCompletionSource<Result<VacationSettings>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _loads.Enqueue(completion);
            return completion;
        }

        public Task<Result<VacationSettings>> LoadAsync(string accountKey, CancellationToken ct)
        {
            Loads.Add(accountKey);
            return _loads.Dequeue().Task;
        }

        public Task<Result<VacationSettings>> SaveAsync(string accountKey, VacationSettings settings, CancellationToken ct)
        {
            Saves.Add(accountKey);
            LastSaved = settings;
            return SaveResult.Task;
        }
    }
}
