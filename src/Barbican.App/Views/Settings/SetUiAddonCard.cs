// Mirrors: Bulwark components/settings/plugins-settings.tsx:196-300 (PluginCard: rounded-lg border p-3 with the name, its badges, the description and the author and version on the left, the switch on the right,
// a click on the left part expands the details under a border-t) and :215-232 (badges: rounded-full px-1.5 py-0.5 text-[0.625rem] font-medium; a problem is destructive, "managed" is cyan, the rest muted). AO-01.

using Barbican.App.Theme;
using Microsoft.UI.Xaml.Automation;

#pragma warning disable MA0051 // a card is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>One add-on in the Add-ons list: a bordered card with a switch and expandable details.</summary>
internal static class SetUiAddonCard
{
    /// <summary>What a badge says and how it is coloured.</summary>
    internal enum BadgeKind
    {
        /// <summary>Muted: "Yours", "Built-in".</summary>
        Quiet,

        /// <summary>The managed (administrator) source.</summary>
        Managed,

        /// <summary>The add-on has a problem.</summary>
        Problem,
    }

    public static Border Build(string name, string description, TextBlock meta, IReadOnlyList<(string Text, BadgeKind Kind)> badges, ToggleSwitch? toggle, UIElement? details, string id, bool error = false)
    {
        var look = StyleState.Current;
        var card = new Border
        {
            BorderBrush = AppResources.Brush(error ? "Destructive" : "Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = look.SetCardRadius,
            Margin = new Thickness(0, 4, 0, 4),
        };
        var stack = new StackPanel();
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 2 };
        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = AppResources.Brush("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (text, kind) in badges)
        {
            chips.Children.Add(Badge(text, kind));
        }

        Grid.SetColumn(chips, 1);
        titleRow.Children.Add(chips);
        info.Children.Add(titleRow);
        if (description.Length > 0)
        {
            info.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = AppResources.Brush("Body"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        meta.TextWrapping = TextWrapping.Wrap;
        info.Children.Add(meta);

        FrameworkElement left = info;
        if (details is not null)
        {
            var expander = new Button
            {
                Content = info,
                Style = look.MenuItemStyle,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(look.Metrics.ButtonRadius),
            };
            AutomationProperties.SetName(expander, name);
            _ = SetUiUi.Id(expander, id + ".expand");
            details.Visibility = Visibility.Collapsed;
            expander.Click += (_, _) => details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            left = expander;
        }

        head.Children.Add(left);
        if (toggle is not null)
        {
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toggle, 1);
            head.Children.Add(toggle);
        }

        var headBox = new Border { Padding = new Thickness(12), Child = head };
        stack.Children.Add(headBox);
        if (details is not null)
        {
            stack.Children.Add(new Border { BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12), Child = details });
        }

        card.Child = stack;
        return SetUiUi.Id(card, id);
    }

    private static Border Badge(string text, BadgeKind kind)
    {
        var (fill, ink) = kind switch
        {
            BadgeKind.Problem => (AppResources.Brush("FeedbackDestructiveHover"), AppResources.Brush("Destructive")),
            BadgeKind.Managed => (AppResources.Brush("Input"), AppResources.Brush("Info")),
            _ => (AppResources.Brush("Input"), AppResources.Brush("Dim")),
        };
        return new Border
        {
            Background = fill,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = ink },
        };
    }
}
