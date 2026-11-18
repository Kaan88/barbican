// Mirrors: see ISrvUiFiltersViewModel. Stub from the M6 plan section 4.11; owner srv-ui replaces the class (constructor dependencies: IFilterService, IAccountManager).

using System.ComponentModel;
using Barbican.Core.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The default <see cref="ISrvUiFiltersViewModel"/>.</summary>
public sealed class SrvUiFiltersViewModel : ISrvUiFiltersViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<SrvUiFilterRow> Rows => throw new PortPendingException("SrvUiFiltersViewModel.Rows");

    public bool Locked => throw new PortPendingException("SrvUiFiltersViewModel.Locked");

    public bool IsLoading => throw new PortPendingException("SrvUiFiltersViewModel.IsLoading");

    public string? LoadError => throw new PortPendingException("SrvUiFiltersViewModel.LoadError");

    public Task LoadAsync(CancellationToken ct) => throw new PortPendingException("SrvUiFiltersViewModel.LoadAsync");

    public Task MoveAsync(string ruleId, int direction, CancellationToken ct) => throw new PortPendingException("SrvUiFiltersViewModel.MoveAsync");

    public Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct) => throw new PortPendingException("SrvUiFiltersViewModel.SetEnabledAsync");

    public Task DeleteAsync(string ruleId, CancellationToken ct) => throw new PortPendingException("SrvUiFiltersViewModel.DeleteAsync");
}
