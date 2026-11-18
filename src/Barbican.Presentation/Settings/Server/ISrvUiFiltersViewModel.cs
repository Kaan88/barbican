// Mirrors: Bulwark filter-settings.tsx (the list: enable, reorder, edit, delete, add; the lock notice of a hand-edited script), M6 plan section 4.11. THREADING (D-M6-00): the view model posts IFilterService.Changed to the UI dispatcher before it raises PropertyChanged (the service raises on a pool thread and its calls complete off the UI thread).
// Contract from the M6 plan; owner srv-ui. The other pages' view models (vacation, identities, templates, folders, recover) are srv-ui's own additions under the SrvUi prefix.

using System.ComponentModel;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The filters page.</summary>
public interface ISrvUiFiltersViewModel : INotifyPropertyChanged
{
    IReadOnlyList<SrvUiFilterRow> Rows { get; }

    /// <summary>The script was hand-edited: the page is read-only and says so (B-SIEVE-007).</summary>
    bool Locked { get; }

    bool IsLoading { get; }

    string? LoadError { get; }

    Task LoadAsync(CancellationToken ct);

    Task MoveAsync(string ruleId, int direction, CancellationToken ct);

    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct);

    Task DeleteAsync(string ruleId, CancellationToken ct);
}
