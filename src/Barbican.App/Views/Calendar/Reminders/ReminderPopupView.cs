// Mirrors: Rust reminders.rs:428 show (the stack of reminders in the window, newest first, four at most) and :499 act (dismiss, snooze:N, open); Bulwark hooks/use-calendar-alerts.ts, components/calendar/missed-reminders-dialog.tsx; I-338, B-CAL-033, B-CAL-035; ADR 0007 (in the window, never a windowed popup).
// Draws IReminderPopup: the stack bottom right while there are reminders, and the missed-reminders dialog on a scrim. Every button is a view model call. The OS notification and the sound are M7's.

using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Calendar.Editors;
using Barbican.Core.Calendar.Reminders;
using Barbican.Presentation.Calendar.Reminders;
using Barbican.Presentation.Compose;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Calendar.Reminders;

/// <summary>The reminder stack and the missed-reminders dialog, one overlay over the whole window.</summary>
public sealed class ReminderPopupView : Grid
{
    /// <summary>Rust showed four at most; the rest wait behind "+N more".</summary>
    public const int MaxShown = 4;

    private readonly IReminderPopup _popup = ViewHost.Get<IReminderPopup>();
    private readonly StackPanel _stack = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 24), Width = 340 };
    private readonly Border _scrim = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly Border _missedCard = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), MaxWidth = 460, Background = AppResources.Brush("Popup") };

    public ReminderPopupView()
    {
        // only the cards take the pointer: the overlay is otherwise transparent to it
        Background = null;
        IsHitTestVisible = true;
        Children.Add(_stack);
        _scrim.Child = _missedCard;
        _missedCard.Tapped += (_, e) => e.Handled = true;
        Children.Add(_scrim);
        AutomationProperties.SetAutomationId(this, "cal.reminders");
        _popup.PropertyChanged += OnChanged;
        Render();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
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

    /// <summary>Redraws both layers from the view model.</summary>
    public void Render()
    {
        RenderStack();
        RenderMissed();
    }

    private void RenderStack()
    {
        _stack.Children.Clear();
        var shown = _popup.Shown;
        _stack.Visibility = shown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var reminder in shown.Take(MaxShown))
        {
            _stack.Children.Add(Card(reminder));
        }

        if (shown.Count > MaxShown)
        {
            _stack.Children.Add(CalendarCardKit.Body("+" + (shown.Count - MaxShown) + " more"));
        }

        if (shown.Count > 1)
        {
            _stack.Children.Add(CalendarCardKit.Btn("Dismiss all", DialogTone.Plain, "cal.reminder.dismiss-all", () => Run(() => _popup.DismissAllAsync(CancellationToken.None))));
        }
    }

    private Border Card(DueReminder reminder)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(CalendarCardKit.Heading((reminder.Task ? "Task: " : string.Empty) + reminder.Title));
        panel.Children.Add(CalendarCardKit.Body(_popup.WhenLabel(reminder)));
        if (reminder.Calendar.Length > 0)
        {
            panel.Children.Add(CalendarCardKit.Label(reminder.Calendar));
        }

        var snooze = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        snooze.Children.Add(CalendarCardKit.Label("Snooze"));
        foreach (var (label, minutes) in new[] { ("5m", 5), ("10m", 10), ("15m", 15), ("1h", 60) })
        {
            var button = CalendarCardKit.Btn(label, DialogTone.Plain, "cal.reminder.snooze." + minutes, () => _popup.Snooze(reminder, minutes));
            button.Height = 28;
            button.FontSize = 12;
            snooze.Children.Add(button);
        }

        panel.Children.Add(snooze);
        panel.Children.Add(CalendarCardKit.Buttons(
            CalendarCardKit.Btn("Dismiss", DialogTone.Plain, "cal.reminder.dismiss", () => Run(() => _popup.DismissAsync(reminder, CancellationToken.None))),
            CalendarCardKit.Btn("Open", DialogTone.Primary, "cal.reminder.open", () => Run(() => _popup.OpenAsync(reminder, CancellationToken.None)))));
        var card = new Border { Background = AppResources.Brush("Popup"), BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Child = panel };
        AutomationProperties.SetAutomationId(card, "cal.reminder");
        AutomationProperties.SetName(card, reminder.Title);
        return card;
    }

    private void RenderMissed()
    {
        var missed = _popup.Missed;
        _scrim.Visibility = missed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _scrim.Background = ViewHost.Get<StyleState>().ScrimBrush;
        if (missed.Count == 0)
        {
            _missedCard.Child = null;
            return;
        }

        var panel = CalendarCardKit.Card("cal.missed", "Missed reminders", 380);
        panel.Children.Add(CalendarCardKit.Body("These came due while Barbican was closed."));
        foreach (var reminder in missed.Take(12))
        {
            var row = new StackPanel { Spacing = 0 };
            row.Children.Add(new TextBlock { Text = (reminder.Task ? "Task: " : string.Empty) + reminder.Title, FontSize = 13.5, Foreground = AppResources.Brush("Body"), TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(CalendarCardKit.Label(_popup.WhenLabel(reminder)));
            AutomationProperties.SetAutomationId(row, "cal.missed.row");
            panel.Children.Add(row);
        }

        if (missed.Count > 12)
        {
            panel.Children.Add(CalendarCardKit.Body("+" + (missed.Count - 12) + " more"));
        }

        panel.Children.Add(CalendarCardKit.Buttons(
            CalendarCardKit.Btn("Close", DialogTone.Plain, "cal.missed.close", () => Run(() => _popup.CloseMissedAsync(false, CancellationToken.None))),
            CalendarCardKit.Btn("Open Calendar", DialogTone.Primary, "cal.missed.open", () => Run(() => _popup.CloseMissedAsync(true, CancellationToken.None)))));
        _missedCard.Child = panel;
    }

    private static void Run(Func<Task> work) => CalendarCardKit.Run(work());
}
