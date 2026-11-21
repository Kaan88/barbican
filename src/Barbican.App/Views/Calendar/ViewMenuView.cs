// Mirrors: Bulwark components/calendar/calendar-toolbar.tsx:70-77 (VIEW_SHORTCUTS: D, W, M, A, K), :126-129 (the menu lists the views in Google Calendar's order: Day, Week, Month, Agenda, Tasks), :466-486 (a radio row per view with its shortcut as a key cap).
// An in-window panel (ADR 0007), not a flyout. WP-12, CA-01.

using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Calendar;

/// <summary>The toolbar's view menu (the dropdown that names the current view).</summary>
public sealed class ViewMenuView : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StackPanel _stack = new();

    public ViewMenuView()
    {
        Content = CalendarMenuKit.Panel(_stack);
        AutomationProperties.SetAutomationId(this, "cal.view.menu");
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Raised when a row was picked (the screen closes the menu).</summary>
    public event EventHandler? Picked;

    public void Show()
    {
        _stack.Children.Clear();
        foreach (var view in CalendarViewMenu.Order(_vm.AvailableViews))
        {
            var chosen = view;
            _stack.Children.Add(CalendarMenuKit.Row("cal.view.menu." + CalendarViewModel.ViewName(view), view.ToString(), null, true, () =>
            {
                Hide();
                Picked?.Invoke(this, EventArgs.Empty);
                _vm.SetView(chosen);
            }, CalendarViewMenu.Shortcut(view), _vm.View == view));
        }

        Visibility = Visibility.Visible;
    }

    public void Hide() => Visibility = Visibility.Collapsed;
}
