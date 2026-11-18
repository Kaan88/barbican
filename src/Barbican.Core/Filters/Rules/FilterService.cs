// Mirrors: see IFilterService. Stub from the M6 plan section 4.6; owner srv-data replaces the class (constructor dependencies: IAccountManager, SieveScriptStore, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Filters.Rules;

/// <summary>The default <see cref="IFilterService"/>.</summary>
public sealed class FilterService : IFilterService
{
    public event EventHandler<FilterChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<FilterState>> LoadAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("FilterService.LoadAsync");

    public Task<Result<FilterState>> SaveAsync(string accountKey, IReadOnlyList<JsonNode> rules, CancellationToken ct) => throw new PortPendingException("FilterService.SaveAsync");

    public Task<Result<FilterScan>> ScanAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct) => throw new PortPendingException("FilterService.ScanAsync");

    public Task<Result<FilterApplied>> ApplyAsync(string accountKey, FilterScan scan, CancellationToken ct) => throw new PortPendingException("FilterService.ApplyAsync");
}
