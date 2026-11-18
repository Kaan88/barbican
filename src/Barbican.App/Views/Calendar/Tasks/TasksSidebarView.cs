// Mirrors: Rust calendar.rs:755 sidebar_tasks (the open tasks under the mini month, soonest due first, capped); Bulwark components/calendar/calendar-sidebar-panel.tsx tasks block; B-CAL-039, T-053 ("Show all (N)").
// Fills the slot SlotNames.CalendarSidebarTasks. Draws TasksViewModel.Sidebar; a tick toggles the task, the title opens it, "Show all" switches to the Tasks view.

using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Calendar.Tasks;

/// <summary>The capped task list of the calendar sidebar.</summary>
public sealed class TasksSidebarView : UserControl
{
    private readonly TasksViewModel _vm = ViewHost.Get<TasksViewModel>();
    private readonly StackPanel _stack = new() { Spacing = 2 };

    public TasksSidebarView()
    {
        Content = _stack;
        AutomationProperties.SetAutomationId(this, "cal.sidebar.tasks");
        AutomationProperties.SetName(this, "Tasks");
        _vm.PropertyChanged += OnChanged;
        Render();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(TasksViewModel.Sidebar), StringComparison.Ordinal) && !string.Equals(e.PropertyName, nameof(TasksViewModel.SidebarHidden), StringComparison.Ordinal) && !string.IsNullOrEmpty(e.PropertyName))
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Render();
        }
        else
        {
            _ = DispatcherQueue.TryEnqueue(Render);
        }
    }

    /// <summary>Redraws the list from the model.</summary>
    public void Render()
    {
        _stack.Children.Clear();
        _stack.Children.Add(new TextBlock { Text = "Tasks", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = AppResources.Brush("Text"), Margin = new Thickness(0, 0, 0, 4) });
        var rows = _vm.Sidebar;
        if (rows.Count == 0)
        {
            _stack.Children.Add(new TextBlock { Text = "Nothing to do.", FontSize = 12.5, Foreground = AppResources.Brush("Faint") });
            return;
        }

        foreach (var row in rows)
        {
            _stack.Children.Add(RowView(row));
        }

        if (_vm.SidebarHidden > 0)
        {
            var all = new Button { Content = "Show all (" + (rows.Count + _vm.SidebarHidden) + ")", Style = (Style)Application.Current.Resources["Button.Action"], Height = 28, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            AutomationProperties.SetAutomationId(all, "cal.sidebar.tasks.all");
            all.Click += (_, _) => CalendarUi.Model.SetView(CalendarViewKind.Tasks);
            _stack.Children.Add(all);
        }
    }

    private Grid RowView(TaskRow row)
    {
        var grid = new Grid { ColumnSpacing = 6, MinHeight = 28 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tick = new Button { Content = row.Done ? "☑" : "☐", Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(4, 0, 4, 0), FontSize = 15 };
        AutomationProperties.SetName(tick, "Toggle done");
        tick.Click += (_, _) => CalendarUi.Run(() => _vm.ToggleAsync(row.Id, CancellationToken.None));
        grid.Children.Add(tick);
        var title = new Button { Content = new TextBlock { Text = row.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = AppResources.Brush("Body") }, Background = null, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
        title.Click += (_, _) => CalendarUi.Run(() => _vm.OpenAsync(row.Id, CancellationToken.None));
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        var due = new TextBlock { Text = row.Due, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = AppResources.Brush(row.DueRank switch { 2 => "RoseFg", 1 => "Accent", _ => "Dim" }) };
        Grid.SetColumn(due, 2);
        grid.Children.Add(due);
        AutomationProperties.SetAutomationId(grid, "cal.sidebar.task");
        return grid;
    }
}
