// Mirrors: Bulwark filter-settings.tsx (the list: enable, reorder, edit, delete, add; the lock notice of a hand-edited script), M6 plan section 4.11. THREADING (D-M6-00): the view model posts IFilterService.Changed to the UI dispatcher before it raises PropertyChanged (the service raises on a pool thread and its calls complete off the UI thread).
// Contract from the M6 plan; owner srv-ui. The members after DeleteAsync are srv-ui's additions (rule editor, Sieve view, apply to existing mail; decision D-M6-D01).

using System.ComponentModel;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The filters page.</summary>
public interface ISrvUiFiltersViewModel : INotifyPropertyChanged
{
    IReadOnlyList<SrvUiFilterRow> Rows { get; }

    string AccountKey { get; }

    /// <summary>The account that owns the currently loaded rules.</summary>
    string? StateAccountKey { get; }

    /// <summary>The script was hand-edited: the page is read-only and says so (B-SIEVE-007).</summary>
    bool Locked { get; }

    bool IsLoading { get; }

    string? LoadError { get; }

    Task LoadAsync(CancellationToken ct);

    Task MoveAsync(string ruleId, int direction, CancellationToken ct);

    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct);

    Task DeleteAsync(string ruleId, CancellationToken ct);

    /// <summary>The failure of the last write (the list was restored), or null.</summary>
    string? SaveError { get; }

    /// <summary>The server's Sieve extensions and whether it advertises reject (the editor offers reject only then).</summary>
    FilterState? State { get; }

    /// <summary>The folders a rule can move or copy to, with their paths.</summary>
    IReadOnlyList<FilterFolder> Folders { get; }

    /// <summary>The rule JSON with this id (a copy), or null.</summary>
    JsonNode? RuleOf(string ruleId);

    /// <summary>Adds the rule, or replaces the one with its id; the list is saved and the script regenerated.</summary>
    Task<bool> UpsertAsync(JsonNode rule, string expectedAccount, string? expectedState, CancellationToken ct);

    /// <summary>The generated Sieve script, read-only (the metadata block, the rules and the hand-written part).</summary>
    string SieveText();

    Task<Result<FilterScan>> ScanAsync(JsonNode rule, long generation, CancellationToken ct);

    Task<Result<FilterApplied>> ApplyAsync(FilterScan scan, CancellationToken ct);
}
