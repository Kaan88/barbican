// Mirrors: Bulwark components/ui/context-menu.tsx:655-667 (CONTEXT_MENU_ITEM_CLASS: px-3 py-1.5, text-sm, gap-2, hover:bg-muted; CONTEXT_MENU_PANEL_CLASS: bg-background rounded-md border shadow-lg, min-w 200), :776-806 (a check or radio row: a 16 px check slot,
// the label, the shortcut as a key cap: ms-auto rounded border px-1.5 text-[0.6875rem] text-muted-foreground), calendar-toolbar.tsx:192-215,466-486 (the More menu and the view menu). Bento and Nordic keep the cards the calendar had. WP-12, CA-01.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a row is one frame of controls built in one pass
namespace Barbican.App.Views.Calendar;

/// <summary>The in-window menu cards of the calendar toolbar (ADR 0007: never a popup).</summary>
internal static class CalendarMenuKit
{
    public static Border Panel(UIElement child)
    {
        var look = ViewHost.Get<StyleState>();
        var bulwark = look.IsBulwark;
        return new Border
        {
            Background = CalendarVisuals.Palette("Popup"),
            BorderBrush = CalendarVisuals.Palette("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = look.CalPopupRadius,
            Padding = new Thickness(bulwark ? 0 : 6),
            MinWidth = bulwark ? 200 : 210,
            Child = child,
        };
    }

    /// <summary>A menu row: an icon (or a check slot), the label, an optional key cap; greyed when <paramref name="enabled"/> is false.</summary>
    public static Button Row(string id, string label, string? icon, bool enabled, Action act, string? shortcut = null, bool? checkedState = null)
    {
        var look = ViewHost.Get<StyleState>();
        var bulwark = look.IsBulwark;
        var grid = new Grid { ColumnSpacing = bulwark ? 8 : 10, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var slot = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        if (checkedState is { } on)
        {
            if (on)
            {
                slot.Children.Add(CalendarVisuals.Icon("Check", 16, 2));
            }
        }
        else if (icon is not null)
        {
            slot.Children.Add(CalendarVisuals.Icon(icon, 16, 1.8));
        }

        grid.Children.Add(slot);
        var text = CalendarVisuals.Text(label, bulwark ? 14 : 13, bulwark ? FontWeights.Normal : FontWeights.SemiBold, CalendarVisuals.Palette(enabled ? (bulwark ? "Text" : "Body") : "Faint"));
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (shortcut is not null)
        {
            var cap = new Border
            {
                BorderBrush = CalendarVisuals.Palette("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = CalendarVisuals.Text(shortcut, 11, FontWeights.Normal, CalendarVisuals.Palette("Dim")),
            };
            Grid.SetColumn(cap, 2);
            grid.Children.Add(cap);
        }

        var button = new Button
        {
            Style = bulwark ? look.MenuItemStyle : (Style)Application.Current.Resources["Button.Icon"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = grid,
            IsEnabled = enabled,
        };
        if (bulwark)
        {
            button.CornerRadius = look.MenuItemRadius;
            button.Padding = new Thickness(12, 6, 12, 6);
        }
        else
        {
            button.Height = 34;
            button.Padding = new Thickness(10, 0, 10, 0);
            button.CornerRadius = new CornerRadius(10);
            button.Foreground = CalendarVisuals.Palette("Dim");
        }

        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => act();
        return button;
    }

    /// <summary>The hairline between two groups of rows (context-menu.tsx ContextMenuSeparator).</summary>
    public static Border Rule() => new() { Height = 1, Background = CalendarVisuals.Palette("Line"), Margin = new Thickness(0, 4, 0, 4) };
}
