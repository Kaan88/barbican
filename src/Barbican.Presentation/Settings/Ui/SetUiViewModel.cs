// Mirrors: see ISetUiViewModel. Stub from the M6 plan section 4.10; owner set-ui replaces the class (constructor dependencies: ISettingValues, ISettingInfo, ISettingsNavigation, IAccountManager).

using System.ComponentModel;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The default <see cref="ISetUiViewModel"/>.</summary>
public sealed class SetUiViewModel : ISetUiViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public string SearchText
    {
        get => throw new PortPendingException("SetUiViewModel.SearchText");
        set => throw new PortPendingException("SetUiViewModel.SearchText");
    }

    public bool ShowAdvanced
    {
        get => throw new PortPendingException("SetUiViewModel.ShowAdvanced");
        set => throw new PortPendingException("SetUiViewModel.ShowAdvanced");
    }

    public IReadOnlyList<SettingsPageInfo> VisiblePages => throw new PortPendingException("SetUiViewModel.VisiblePages");

    public IReadOnlyList<SettingSearchHit> SearchHits => throw new PortPendingException("SetUiViewModel.SearchHits");

    public IReadOnlyList<SetUiRow> RowsOf(string pageId) => throw new PortPendingException("SetUiViewModel.RowsOf");

    public Task SetValueAsync(string rowId, JsonNode? value, CancellationToken ct) => throw new PortPendingException("SetUiViewModel.SetValueAsync");

    public Task RefreshInfoAsync(string pageId, CancellationToken ct) => throw new PortPendingException("SetUiViewModel.RefreshInfoAsync");
}
