using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Mail.Archive;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Tests.Accounts;

#pragma warning disable VSTHRD003 // The test deliberately controls the delayed scan.

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiFilterRecoverAccountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scan_and_editor_from_old_generation_cannot_apply_after_a_b_a()
    {
        var (accounts, a, b) = Accounts();
        var service = new DelayedFilter();
        var page = new SrvUiFiltersViewModel(service, accounts, new InlineDispatcher());
        await page.LoadAsync(Ct);
        var rule = page.RuleOf(Assert.Single(page.Rows).Id)!;
        var generation = page.AccountGeneration;
        var editor = new SrvUiFilterEditorModel(page, rule) { Name = "Old" };
        var scan = page.ScanAsync(rule, 1, a.Key, generation, Ct);
        await accounts.SwitchToAsync(b.Key, Ct);
        await accounts.SwitchToAsync(a.Key, Ct);
        Assert.True(SpinWait.SpinUntil(() => string.Equals(page.StateAccountKey, a.Key, StringComparison.Ordinal) && !page.IsLoading, TimeSpan.FromSeconds(5)));
        service.ScanGate.SetResult(Result<FilterScan>.Ok(new FilterScan(rule, [], 1, null, 1, "state")));
        var stale = await scan;
        Assert.False(stale.IsOk);
        var applied = await page.ApplyAsync(new FilterScan(rule, [], 1, null, 1, "state"), a.Key, generation, Ct);
        Assert.False(applied.IsOk);
        await editor.SaveAsync(Ct);
        Assert.Null(editor.Saved);
        Assert.Equal(0, service.Writes);
        Assert.Equal(0, service.Applies);
    }

    [Fact]
    public async Task Recovery_rejects_an_old_row_even_if_ids_collide_after_switch()
    {
        var (accounts, a, b) = Accounts();
        var source = new ControlledRecover();
        var page = new SrvUiRecoverViewModel(source, accounts, new InlineDispatcher());
        await page.LoadAsync(Ct);
        var oldGeneration = page.AccountGeneration;
        Assert.Equal("A", Assert.Single(page.Items).Subject);
        await accounts.SwitchToAsync(b.Key, Ct);
        Assert.True(SpinWait.SpinUntil(() => page.Items.Any(x => string.Equals(x.Subject, "B", StringComparison.Ordinal)), TimeSpan.FromSeconds(5)));
        await page.RestoreAsync("same-id", oldGeneration, Ct);
        Assert.Empty(source.Restores);
        await page.RestoreAsync("same-id", page.AccountGeneration, Ct);
        Assert.Equal(b.Key, Assert.Single(source.Restores));
        Assert.NotEqual(a.Key, source.Restores[0], StringComparer.Ordinal);
    }

    private static (FakeAccounts Accounts, AccountInfo A, AccountInfo B) Accounts()
    {
        var a = FakeAccounts.Info("a@example.org"); var b = FakeAccounts.Info("b@example.org"); var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(a, true, true, true, null, null), new AccountStatus(b, false, false, true, null, null)); return (accounts, a, b);
    }

    private sealed class DelayedFilter : IFilterService
    {
        private readonly SampleFilterService _inner = new();
        public int Writes { get; private set; }
        public int Applies { get; private set; }
        public TaskCompletionSource<Result<FilterScan>> ScanGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<FilterChangedEventArgs>? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public Task<Result<FilterState>> LoadAsync(string key, CancellationToken ct) => _inner.LoadAsync(key, ct);
        public Task<Result<FilterState>> SaveAsync(string key, IReadOnlyList<JsonNode> rules, string? state, CancellationToken ct) { Writes++; return _inner.SaveAsync(key, rules, state, ct); }
        public Task<Result<FilterScan>> ScanAsync(string key, JsonNode rule, long generation, CancellationToken ct) => ScanGate.Task;
        public Task<Result<FilterApplied>> ApplyAsync(string key, FilterScan scan, CancellationToken ct) { Applies++; return _inner.ApplyAsync(key, scan, ct); }
    }

    private sealed class ControlledRecover : ISrvUiRecoverSource
    {
        public List<string> Restores { get; } = [];
        public bool Available(string accountKey) => true;
        public Task<Result<IReadOnlyList<ArchivedItem>>> ListAsync(string accountKey, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<ArchivedItem>>.Ok([new("same-id", accountKey.Contains("a@example.org", StringComparison.Ordinal) ? "A" : "B", null)]));
        public Task<Result<Unit>> RestoreAsync(string accountKey, string id, CancellationToken ct) { Restores.Add(accountKey); return Task.FromResult(Result<Unit>.Ok(default)); }
    }
}
