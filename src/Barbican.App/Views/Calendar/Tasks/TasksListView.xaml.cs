// Mirrors: Rust cal_extra.rs render_tasks_view; Bulwark components/calendar/task-list-view.tsx, task-toolbar.tsx, tasks-sidebar-filters.tsx; B-CAL-007, 036..039; I-342..I-344. Draws TasksViewModel; every action is a view model call (toggle, open, quick add, day filter) and the row menu is MENU-TASK.

using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Calendar.Tasks;

/// <summary>The Tasks view.</summary>
public sealed partial class TasksListView : UserControl
{
    private readonly TasksViewModel _vm = ViewHost.Get<TasksViewModel>();
    private readonly List<Button> _chips = [];
    private bool _syncingQuick;
    private bool _bulwark;

    public TasksListView()
    {
        InitializeComponent();
        for (var i = 0; i < TasksViewModel.FilterNames.Count; i++)
        {
            var index = i;
            var chip = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 30, CornerRadius = new CornerRadius(15), FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0) };
            AutomationProperties.SetAutomationId(chip, "cal.tasks.filter." + TasksViewModel.FilterNames[i].ToLowerInvariant());
            chip.Click += (_, _) => _vm.Filter = index;
            _chips.Add(chip);
            Filters.Children.Add(chip);
        }

        _vm.PropertyChanged += OnModelChanged;
        _vm.Rows.CollectionChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
        Render();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            Render();
        }
        else
        {
            _ = DispatcherQueue.TryEnqueue(Render);
        }
    }

    /// <summary>Redraws the filters, the quick-add hint, the day chip and the rows from the model in one pass.</summary>
    public void Render()
    {
        var bulwark = CalendarVisuals.Bulwark;
        var look = ViewHost.Get<Barbican.App.Theme.StyleState>();
        ApplyLook(bulwark, look);
        var counts = _vm.Counts;
        for (var i = 0; i < _chips.Count; i++)
        {
            var n = i < counts.Count ? counts[i] : 0;
            var on = i == _vm.Filter;
            _chips[i].Content = TasksViewModel.FilterNames[i] + " " + n;
            if (bulwark)
            {
                // a bordered segment, the chosen one on the primary fill (task-toolbar.tsx:37-52)
                _chips[i].Style = (Style)Application.Current.Resources["Button.Icon"];
                _chips[i].Background = on ? CalendarVisuals.Palette("Accent") : null;
                _chips[i].Foreground = CalendarVisuals.Palette(on ? "OnAccent" : "Dim");
                _chips[i].BorderThickness = new Thickness(0, 0, i < _chips.Count - 1 ? 1 : 0, 0);
                _chips[i].BorderBrush = CalendarVisuals.Palette("Line");
                _chips[i].CornerRadius = new CornerRadius(0);
                _chips[i].Height = 32;
                _chips[i].FontSize = 12;
                _chips[i].FontWeight = Microsoft.UI.Text.FontWeights.Medium;
                _chips[i].Padding = new Thickness(12, 0, 12, 0);
            }
            else
            {
                _chips[i].Style = (Style)Application.Current.Resources[on ? "Button.Primary" : "Button.Action"];
                _chips[i].ClearValue(Control.BackgroundProperty);
                _chips[i].ClearValue(Control.ForegroundProperty);
                _chips[i].BorderThickness = new Thickness(1);
                _chips[i].Height = 30;
                _chips[i].FontSize = 12.5;
                _chips[i].CornerRadius = new CornerRadius(15);
                _chips[i].Padding = new Thickness(12, 0, 12, 0);
            }
        }

        if (!string.Equals(Quick.Text, _vm.Quick, StringComparison.Ordinal))
        {
            _syncingQuick = true;
            Quick.Text = _vm.Quick;
            _syncingQuick = false;
        }

        QuickDue.Text = _vm.QuickDueLabel;
        DayChip.Visibility = _vm.Day is null ? Visibility.Collapsed : Visibility.Visible;
        DayChip.Content = _vm.DayLabel + "  ✕";
        var items = new List<TaskRowItem>(_vm.Rows.Count);
        foreach (var row in _vm.Rows)
        {
            items.Add(new TaskRowItem(row, string.Equals(row.Id, _vm.SelectedId, StringComparison.Ordinal)));
        }

        List.ItemsSource = items;
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // the toolbar row, the quick-add row and the list rows follow the style (rebuilt only when it changes)
    private void ApplyLook(bool bulwark, Barbican.App.Theme.StyleState look)
    {
        if (_bulwark != bulwark || List.ItemTemplate is null)
        {
            _bulwark = bulwark;
            List.ItemTemplate = (DataTemplate)Resources[bulwark ? "Row.Bulwark" : "Row.Legacy"];
            List.ItemContainerStyle = (Style)(bulwark ? Application.Current.Resources["ListItem.Row"] : Resources["Row.Legacy.Container"]);
        }

        Toolbar.Padding = bulwark ? new Thickness(16, 0, 16, 0) : new Thickness(0);
        Toolbar.Height = bulwark ? look.CalToolbarHeight + 1 : double.NaN;
        Toolbar.BorderBrush = CalendarVisuals.Palette("Line");
        Toolbar.BorderThickness = bulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        FilterBox.BorderBrush = CalendarVisuals.Palette("Line");
        FilterBox.BorderThickness = bulwark ? new Thickness(1) : new Thickness(0);
        FilterBox.CornerRadius = bulwark ? look.ButtonRadius : new CornerRadius(0);
        FilterBox.VerticalAlignment = VerticalAlignment.Center;
        Filters.Spacing = bulwark ? 0 : 6;
        CreateButton.Visibility = bulwark ? Visibility.Visible : Visibility.Collapsed;
        CreateButton.CornerRadius = look.ButtonRadius;
        QuickBox.Padding = bulwark ? new Thickness(16, 0, 16, 8) : new Thickness(0);
        QuickBox.BorderBrush = CalendarVisuals.Palette("Line");
        QuickBox.BorderThickness = bulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        DueRow.Margin = bulwark ? new Thickness(16, 0, 16, 0) : new Thickness(0);
        Root.RowSpacing = bulwark ? 0 : 8;
        DueRow.Visibility = bulwark && _vm.QuickDueLabel.Length == 0 && _vm.Day is null ? Visibility.Collapsed : Visibility.Visible;
        AddButton.CornerRadius = bulwark ? look.ButtonRadius : new CornerRadius(16);
    }

    private void OnCreateTask(object sender, RoutedEventArgs e) =>
        CalendarUi.Run(() => ViewHost.Get<Barbican.Presentation.Calendar.Editors.ICalendarEditors>().OpenNewTaskAsync(_vm.DefaultList(), null, CancellationToken.None));

    private void OnQuickChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncingQuick)
        {
            _vm.Quick = Quick.Text;
        }
    }

    private void OnQuickKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            Add();
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e) => Add();

    private void Add() => CalendarUi.Run(() => _vm.QuickAddAsync(CancellationToken.None));

    private void OnClearDay(object sender, RoutedEventArgs e) => CalendarUi.Model.ToggleTaskDay("clear");

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskRowItem item)
        {
            _vm.Select(item.Row.Id);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item(e.OriginalSource) is { } item)
        {
            CalendarUi.Run(() => _vm.OpenAsync(item.Row.Id, CancellationToken.None));
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (Item(e.OriginalSource) is { } item)
        {
            _vm.Select(item.Row.Id);
            var p = e.GetPosition(null);
            CalendarUi.ShowMenu(CalendarMenuIds.Task, p.X, p.Y, item.Row.Id);
        }
    }

    private void OnTick(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            CalendarUi.Run(() => _vm.ToggleAsync(item.Row.Id, CancellationToken.None));
        }
    }

    private static TaskRowItem? Item(object source) => (source as FrameworkElement)?.DataContext as TaskRowItem;
}
