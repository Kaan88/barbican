// Two editors with no change notification must not overwrite one another's loaded filter version.
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Accounts;
using Barbican.Presentation.Tests.Accounts;
using Barbican.Core.Filters.Rules;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Server;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class FilterVersionTests
{
    [Fact]
    public async Task A_stale_editor_keeps_its_rows_and_reports_the_conflict()
    {
        var service = new SilentService();
        var first = new SrvUiFiltersViewModel(service, null, new InlineDispatcher());
        var second = new SrvUiFiltersViewModel(service, null, new InlineDispatcher());
        var ct = TestContext.Current.CancellationToken;
        await first.LoadAsync(ct);
        await second.LoadAsync(ct);
        var id = Assert.Single(first.Rows).Id;
        await first.SetEnabledAsync(id, false, ct);
        Assert.False(Assert.Single(first.Rows).Enabled);
        await second.DeleteAsync(id, ct);
        Assert.NotNull(second.SaveError);
        Assert.True(Assert.Single(second.Rows).Enabled);
        var remote = await service.LoadAsync("sample", ct);
        Assert.False((bool?)Assert.Single(remote.Value!.Rules)["enabled"]);
    }

    [Fact]
    public async Task An_open_modal_rejects_stale_edits_after_a_real_change_notification()
    {
        var service = new SampleFilterService();
        var page = new SrvUiFiltersViewModel(service, null, new InlineDispatcher());
        var other = new SrvUiFiltersViewModel(service, null, new InlineDispatcher());
        var ct = TestContext.Current.CancellationToken;
        await page.LoadAsync(ct);
        await other.LoadAsync(ct);
        var id = Assert.Single(page.Rows).Id;
        var editor = new SrvUiFilterEditorModel(page, page.RuleOf(id)) { Name = "Stale edit" };
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PropertyChanged += (_, e) => { if (string.Equals(e.PropertyName, nameof(page.Rows), StringComparison.Ordinal) && page.Rows.Count == 1 && !page.Rows[0].Enabled) reloaded.TrySetResult(); };
        await other.SetEnabledAsync(id, false, ct);
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        await editor.SaveAsync(ct);
        Assert.Null(editor.Saved);
        Assert.Contains("changed", editor.Error!, StringComparison.OrdinalIgnoreCase);
        var remote = await service.LoadAsync("sample", ct);
        Assert.Equal("Newsletters", (string?)Assert.Single(remote.Value!.Rules)["name"]);
        Assert.False((bool?)Assert.Single(remote.Value.Rules)["enabled"]);
    }

    [Fact]
    public async Task An_open_modal_cannot_save_after_switching_accounts()
    {
        var accounts = new FakeAccounts();
        var a = FakeAccounts.Info("a@example.org");
        var b = FakeAccounts.Info("b@example.org");
        accounts.Set(new AccountStatus(a, true, false, true, null, null), new AccountStatus(b, false, false, true, null, null));
        var service = new SampleFilterService();
        var page = new SrvUiFiltersViewModel(service, accounts, new InlineDispatcher());
        var ct = TestContext.Current.CancellationToken;
        await page.LoadAsync(ct);
        var editor = new SrvUiFilterEditorModel(page, page.RuleOf(Assert.Single(page.Rows).Id)) { Name = "Wrong account" };
        await accounts.SwitchToAsync(b.Key, ct);
        var duringSwitch = new SrvUiFilterEditorModel(page, page.RuleOf(Assert.Single(page.Rows).Id)) { Name = "Stale account state" };
        await page.LoadAsync(ct);
        await editor.SaveAsync(ct);
        await duringSwitch.SaveAsync(ct);
        Assert.Null(editor.Saved);
        Assert.Null(duringSwitch.Saved);
        Assert.Contains("changed", editor.Error!, StringComparison.OrdinalIgnoreCase);
        var remote = await service.LoadAsync(b.Key, ct);
        Assert.Equal("Newsletters", (string?)Assert.Single(remote.Value!.Rules)["name"]);
    }

    private sealed class SilentService : IFilterService
    {
        private readonly SampleFilterService _inner = new();
        public event EventHandler<FilterChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<FilterState>> LoadAsync(string accountKey, CancellationToken ct) => _inner.LoadAsync(accountKey, ct);
        public Task<Result<FilterState>> SaveAsync(string accountKey, IReadOnlyList<JsonNode> rules, string? expectedState, CancellationToken ct) => _inner.SaveAsync(accountKey, rules, expectedState, ct);
        public Task<Result<FilterScan>> ScanAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct) => _inner.ScanAsync(accountKey, rule, generation, ct);
        public Task<Result<FilterApplied>> ApplyAsync(string accountKey, FilterScan scan, CancellationToken ct) => _inner.ApplyAsync(accountKey, scan, ct);
    }
}
