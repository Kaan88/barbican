// Mirrors: Bulwark components/calendar/calendar-toolbar.tsx (the More menu: Import, Subscribe, Refresh, Print); Rust app.slint calendar toolbar; I-312. An in-window panel (ADR 0007), not a flyout.
// Refresh and Print are the surface's own; Import and Subscribe run cal-ui's commands (calendar.import, calendar.subscribe) and are greyed until those are registered.

using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;

namespace Barbican.App.Views.Calendar;

/// <summary>The toolbar's More menu.</summary>
public sealed class MoreMenuView : UserControl
{
    private readonly StackPanel _stack = new() { Spacing = 2 };

    public MoreMenuView()
    {
        Content = new Border
        {
            Background = CalendarVisuals.Palette("Popup"),
            BorderBrush = CalendarVisuals.Palette("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            MinWidth = 210,
            Child = _stack,
        };
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
        _stack.Children.Add(Row("cal.more.refresh", "Refresh", "Refresh", true, () => RefreshRequested?.Invoke(this, EventArgs.Empty)));
        _stack.Children.Add(Row("cal.more.print", "Print", "Print", true, () => PrintRequested?.Invoke(this, EventArgs.Empty)));
        _stack.Children.Add(Row("cal.more.import", "Import events (.ics)", "Calendar", CalendarUi.CanRun("calendar.import"), () => CalendarUi.RunCommand("calendar.import")));
        _stack.Children.Add(Row("cal.more.subscribe", "Subscribe to a calendar", "Cal", CalendarUi.CanRun("calendar.subscribe"), () => CalendarUi.RunCommand("calendar.subscribe")));
        Visibility = Visibility.Visible;
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    private Button Row(string id, string label, string icon, bool enabled, Action act)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { CalendarVisuals.Icon(icon, 16, 1.8), CalendarVisuals.Text(label, 13, FontWeights.SemiBold, CalendarVisuals.Palette(enabled ? "Body" : "Faint")) } };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 34,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(10),
            Content = content,
            Foreground = CalendarVisuals.Palette("Dim"),
            IsEnabled = enabled,
        };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            Hide();
            Picked?.Invoke(this, EventArgs.Empty);
            act();
        };
        return button;
    }
}
