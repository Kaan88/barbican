// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IFilterService that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="IFilterService"/> with one rule; a scan finds two messages and an apply updates them.</summary>
public sealed class SampleFilterService : IFilterService
{
    private readonly Lock _lock = new();
    private long _version;
    private List<JsonNode> _rules =
    [
        new JsonObject
        {
            ["id"] = "bw-sample",
            ["name"] = "Newsletters",
            ["enabled"] = true,
            ["matchType"] = "all",
            ["conditions"] = new JsonArray(new JsonObject { ["field"] = "from", ["comparator"] = "domain_is", ["value"] = "news.example" }),
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "mark_read" }),
        },
    ];

    public event EventHandler<FilterChangedEventArgs>? Changed;

    public Task<Result<FilterState>> LoadAsync(string accountKey, CancellationToken ct) => SampleThreading.OffThreadAsync(() => Result<FilterState>.Ok(State()));

    public async Task<Result<FilterState>> SaveAsync(string accountKey, IReadOnlyList<JsonNode> rules, string? expectedState, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var state = await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                if (!string.Equals(expectedState, _version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    return Result<FilterState>.Fail(new ValidationError("Filters changed; reload before saving."));
                _rules = [.. rules.Select(r => r.DeepClone())];
                _version++;
                return Result<FilterState>.Ok(State());
            }
        }).ConfigureAwait(false);
        if (state.IsOk) SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new FilterChangedEventArgs(accountKey)));
        return state;
    }

    public Task<Result<FilterScan>> ScanAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<FilterScan>.Ok(new FilterScan(rule, [new FilterPlanStep("keyword", ["m1", "m2"], "$seen")], 2, null, generation, "s1")));

    public Task<Result<FilterApplied>> ApplyAsync(string accountKey, FilterScan scan, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<FilterApplied>.Ok(new FilterApplied(FilterAppliedKind.Done, scan?.Total ?? 0, null)));

    private FilterState State()
    {
        lock (_lock)
        {
            return new FilterState([.. _rules.Select(r => r.DeepClone())], string.Empty, false, ["fileinto", "reject"], true) { State = _version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }
    }
}
