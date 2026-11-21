// Mirrors: Bulwark components/calendar/calendar-toolbar.tsx:192-215 (the More menu: Import, Subscribe, a rule, Refresh, Print), components/ui/context-menu.tsx:655-667 (the panel and the rows); Rust app.slint calendar toolbar; I-312. An in-window panel (ADR 0007), not a flyout.
// Refresh and Print are the surface's own; Import and Subscribe run cal-ui's commands (calendar.import, calendar.subscribe) and are greyed until those are registered.

using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Calendar;

/// <summary>The toolbar's More menu.</summary>
public sealed class MoreMenuView : UserControl
{
    private readonly StackPanel _stack = new();

    public MoreMenuView()
    {
        Content = CalendarMenuKit.Panel(_stack);
        AutomationProperties.SetAutomationId(this, "cal.more.menu");
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Raised when a row was picked (the screen closes the menu).</summary>
    public event EventHandler? Picked;

    /// <summary>The Print row was picked.</summary>
    public event EventHandler? PrintRequested;

    /// <summary>The Refresh row was picked.</summary>
    public event EventHandler? RefreshRequested;

    public void Show()
    {
        _stack.Children.Clear();
        var bulwark = CalendarVisuals.Bulwark;
        if (bulwark)
        {
            // calendar-toolbar.tsx:203-208: Import, Subscribe, a rule, Refresh, Print
            _stack.Children.Add(Row("cal.more.import", "Import events (.ics)", "Download", CalendarUi.CanRun("calendar.import"), () => CalendarUi.RunCommand("calendar.import")));
            _stack.Children.Add(Row("cal.more.subscribe", "Subscribe to a calendar", "External", CalendarUi.CanRun("calendar.subscribe"), () => CalendarUi.RunCommand("calendar.subscribe")));
            _stack.Children.Add(CalendarMenuKit.Rule());
            _stack.Children.Add(Row("cal.more.refresh", "Refresh", "Refresh", true, () => RefreshRequested?.Invoke(this, EventArgs.Empty)));
            _stack.Children.Add(Row("cal.more.print", "Print", "Print", true, () => PrintRequested?.Invoke(this, EventArgs.Empty)));
        }
        else
        {
            _stack.Spacing = 2;
            _stack.Children.Add(Row("cal.more.refresh", "Refresh", "Refresh", true, () => RefreshRequested?.Invoke(this, EventArgs.Empty)));
            _stack.Children.Add(Row("cal.more.print", "Print", "Print", true, () => PrintRequested?.Invoke(this, EventArgs.Empty)));
            _stack.Children.Add(Row("cal.more.import", "Import events (.ics)", "Calendar", CalendarUi.CanRun("calendar.import"), () => CalendarUi.RunCommand("calendar.import")));
            _stack.Children.Add(Row("cal.more.subscribe", "Subscribe to a calendar", "Cal", CalendarUi.CanRun("calendar.subscribe"), () => CalendarUi.RunCommand("calendar.subscribe")));
        }

        Visibility = Visibility.Visible;
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    private Button Row(string id, string label, string icon, bool enabled, Action act) =>
        CalendarMenuKit.Row(id, label, icon, enabled, () =>
        {
            Hide();
            Picked?.Invoke(this, EventArgs.Empty);
            act();
        });
}
