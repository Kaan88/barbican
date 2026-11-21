// Mirrors: Bulwark components/calendar/calendar-app.tsx:2877 (the hover popover: enterable, shows the title, when, where and calendar), setting calendarHoverPreview (off, instant, 500 ms, 1 s, 2 s); Rust calendar.rs:151 hover_delay; B-CAL-054 (the panel's date format); I-299.
// An in-window card (never a popup window, ADR 0007): the screen's overlay layer holds it, and it is hit-testable so the pointer can enter it without it closing.

using Microsoft.UI.Xaml.Automation;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;

namespace Barbican.App.Views.Calendar;

/// <summary>The hover preview card.</summary>
public sealed class HoverCardView : UserControl
{
    private readonly Border _box = new();

    public HoverCardView()
    {
        _box.Background = CalendarVisuals.Palette("Popup");
        _box.BorderBrush = CalendarVisuals.Palette("Line");
        _box.BorderThickness = new Thickness(1);
        _box.CornerRadius = new CornerRadius(14);
        _box.Padding = new Thickness(12, 10, 12, 10);
        _box.Width = 250;
        Content = _box;
        AutomationProperties.SetAutomationId(this, "cal.hover");
        AutomationProperties.SetName(this, "Event preview");
        Visibility = Visibility.Collapsed;
        PointerEntered += (_, _) => Entered?.Invoke(this, EventArgs.Empty);
        PointerExited += (_, _) => Left?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The pointer is on the card: it stays.</summary>
    public event EventHandler? Entered;

    public event EventHandler? Left;

    /// <summary>The occurrence on show, or null.</summary>
    public string? ShownId { get; private set; }

    public void Show(EventDetail d)
    {
        ShownId = d.Id;
        // Bulwark's popover is a rounded-lg card on the page ground (event-detail-popover.tsx:392); the others keep the 14 px card
        var look = Barbican.App.Shell.ViewHost.Get<Barbican.App.Theme.StyleState>();
        _box.CornerRadius = look.IsBulwark ? look.PopupRadius : new CornerRadius(14);
        _box.Background = CalendarVisuals.Palette(look.IsBulwark ? "Bg" : "Popup");
        var stack = new StackPanel { Spacing = 3 };
        var dot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        dot.Children.Add(new Border { Width = look.IsBulwark ? 16 : 10, Height = look.IsBulwark ? 16 : 10, CornerRadius = new CornerRadius(4), Background = CalendarVisuals.SolidBrush(d.CalendarColor), VerticalAlignment = VerticalAlignment.Center });
        dot.Children.Add(CalendarVisuals.Text(d.Title, look.IsBulwark ? 14 : 13.5, look.IsBulwark ? FontWeights.SemiBold : FontWeights.Bold, CalendarVisuals.Palette("Text")));
        stack.Children.Add(dot);
        stack.Children.Add(CalendarVisuals.Text(d.When, 12, FontWeights.Normal, CalendarVisuals.Palette("Body")));
        if (d.Location.Length > 0)
        {
            stack.Children.Add(CalendarVisuals.Text(d.Location, 12, FontWeights.Normal, CalendarVisuals.Palette("Dim")));
        }

        if (d.CalendarName.Length > 0)
        {
            stack.Children.Add(CalendarVisuals.Text(d.CalendarName, 11.5, FontWeights.Normal, CalendarVisuals.Palette("Faint")));
        }

        if (d.Guests.Count > 0)
        {
            stack.Children.Add(CalendarVisuals.Text(d.Guests.Count + (d.Guests.Count == 1 ? " guest" : " guests"), 11.5, FontWeights.Normal, CalendarVisuals.Palette("Faint")));
        }

        _box.Child = stack;
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        ShownId = null;
        Visibility = Visibility.Collapsed;
    }
}
