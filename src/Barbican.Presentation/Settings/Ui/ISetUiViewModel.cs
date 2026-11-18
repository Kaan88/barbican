// Mirrors: Bulwark settings-app.tsx (the search box, tabs, "Show advanced settings"), M6 plan section 4.10. THREADING (D-M6-00): the view model posts ISettingValues.Changed, ISettingBackup.Changed and ISettingInfo answers to the UI dispatcher before it raises PropertyChanged; the data services raise on pool threads.
// Contract from the M6 plan; owner set-ui.

using System.ComponentModel;
using System.Text.Json.Nodes;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The state of the settings screen apart from the page that is open (that is ISettingsNavigation).</summary>
public interface ISetUiViewModel : INotifyPropertyChanged
{
    string SearchText { get; set; }

    bool ShowAdvanced { get; set; }

    /// <summary>The navigation entries that show now (advanced pages only with ShowAdvanced; pages of a capability the login lacks are hidden).</summary>
    IReadOnlyList<SettingsPageInfo> VisiblePages { get; }

    /// <summary>The hits of SearchText (empty when it is empty), best first.</summary>
    IReadOnlyList<SettingSearchHit> SearchHits { get; }

    /// <summary>The rows of a catalog page with their values.</summary>
    IReadOnlyList<SetUiRow> RowsOf(string pageId);

    Task SetValueAsync(string rowId, JsonNode? value, CancellationToken ct);

    /// <summary>Loads the text of the info rows of a page (off the UI thread); PropertyChanged tells the screen.</summary>
    Task RefreshInfoAsync(string pageId, CancellationToken ct);
}
