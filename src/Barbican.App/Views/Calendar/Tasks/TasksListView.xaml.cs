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
        var counts = _vm.Counts;
        for (var i = 0; i < _chips.Count; i++)
        {
            var n = i < counts.Count ? counts[i] : 0;
            _chips[i].Content = TasksViewModel.FilterNames[i] + " " + n;
            _chips[i].Style = (Style)Application.Current.Resources[i == _vm.Filter ? "Button.Primary" : "Button.Action"];
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
