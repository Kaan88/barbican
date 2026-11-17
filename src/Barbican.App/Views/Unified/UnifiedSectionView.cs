// Mirrors: Bulwark components/layout/sidebar.tsx (the "All inboxes" row with its unread badge above the folders), Rust main.slint unified
// row. spec I-050, I-051. Owner unified. Built in code (no XAML of its own): one row in the sidebar's unified slot; collapsed while the
// setting is off, so the sidebar costs nothing extra.

using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.Presentation.Unified;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Unified;

/// <summary>The unified inbox row of the sidebar.</summary>
public sealed partial class UnifiedSectionView : UserControl
{
    private readonly UnifiedSectionViewModel _vm;
    private readonly TextBlock _badge;
    private readonly Button _row;

    public UnifiedSectionView()
    {
        _vm = ViewHost.Get<UnifiedSectionViewModel>();
        var glyph = new FontIcon { Glyph = "", FontSize = 14, Margin = new Thickness(0, 0, 10, 0) };
        var title = new TextBlock { Text = UnifiedSectionViewModel.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _badge = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, Margin = new Thickness(8, 0, 0, 0) };
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(_badge, 2);
        grid.Children.Add(glyph);
        grid.Children.Add(title);
        grid.Children.Add(_badge);
        _row = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6, 12, 6),
        };
        AutomationProperties.SetName(_row, UnifiedSectionViewModel.Title);
        AutomationProperties.SetAutomationId(_row, "sidebar-unified");
        _row.Click += OnClick;
        Content = _row;
        Apply();
        _vm.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _vm.PropertyChanged -= OnChanged;
    }

#pragma warning disable VSTHRD100 // a click handler returns void; the list reports a failed load itself
    private async void OnClick(object sender, RoutedEventArgs e) => await _vm.OpenAsync();
#pragma warning restore VSTHRD100

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    // the old content stays until the new values are set: no blanking while the badge changes
    private void Apply()
    {
        Visibility = _vm.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        _badge.Text = _vm.Badge;
        _row.FontWeight = _vm.IsOpen ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }
}
