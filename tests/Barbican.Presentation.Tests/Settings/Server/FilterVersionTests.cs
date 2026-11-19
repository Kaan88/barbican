// Two editors with no change notification must not overwrite one another's loaded filter version.
using System.Text.Json.Nodes;
using Barbican.Core.Common;
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
