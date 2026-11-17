// Mirrors: Bulwark components/layout/sidebar.tsx (the "All inboxes" row with its unread badge above the folders; per-account rows under it, :2889), Rust main.slint unified row.
// spec I-050, I-051, I-053. Owner unified. One row in the sidebar unified slot; collapsed while the setting is off, so the sidebar costs nothing extra.

using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Unified;

namespace Barbican.App.Views.Unified;

/// <summary>The unified inbox row of the sidebar, and the accounts it reads.</summary>
public sealed partial class UnifiedSectionView : UserControl
{
    private readonly UnifiedSectionViewModel _vm;

    public UnifiedSectionView()
    {
        _vm = ViewHost.Get<UnifiedSectionViewModel>();
        AccountsModel = ViewHost.Get<AccountsViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Apply();

        // follows the view model while in the tree; a slot that is unloaded and loaded again (pane collapse, theme) subscribes again
        Loaded += (_, _) =>
        {
            _vm.PropertyChanged -= OnChanged;
            _vm.PropertyChanged += OnChanged;
            AccountsModel.Rows.CollectionChanged -= OnRowsChanged;
            AccountsModel.Rows.CollectionChanged += OnRowsChanged;
            Look.PropertyChanged -= OnLookChanged;
            Look.PropertyChanged += OnLookChanged;
            Apply();
        };
        Unloaded += (_, _) =>
        {
            _vm.PropertyChanged -= OnChanged;
            AccountsModel.Rows.CollectionChanged -= OnRowsChanged;
            Look.PropertyChanged -= OnLookChanged;
        };
    }

    public AccountsViewModel AccountsModel { get; }

    public StyleState Look { get; }

#pragma warning disable VSTHRD100 // a click handler returns void; the list reports a failed load itself
    private async void OnClick(object sender, RoutedEventArgs e) => await _vm.OpenAsync();
#pragma warning restore VSTHRD100

    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountRowViewModel row })
        {
            // the same switch the popover runs: it reports its own failure and turns a refused account into the sign-in form
            AccountsModel.SwitchCommand.Execute(row);
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void OnLookChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void OnRowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Apply();

    // the old content stays until the new values are set: no blanking while the badge changes
    private void Apply()
    {
        Visibility = _vm.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        Badge.Text = _vm.Badge;
        Row.Background = _vm.IsOpen ? Fmt.Palette("Sel") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Row.Foreground = Fmt.Palette(_vm.IsOpen ? "SelText" : "Body");
        Row.FontWeight = _vm.IsOpen ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        AccountRows.Visibility = _vm.IsVisible && _vm.ListsAccounts && AccountsModel.Rows.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
}
