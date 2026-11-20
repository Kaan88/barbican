// Mirrors: M6 plan section 6.2 (the settings views); the controls the pages share: text, pill buttons, cards, the guarded async handler. Every popup is in-window (ADR 0007): dialogs go through IDialogService.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Settings.Ui;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Settings;

/// <summary>What the settings views share.</summary>
public static class SetUiUi
{
    public static ISetUiViewModel Model => ViewHost.Get<ISetUiViewModel>();

    /// <summary>The app name the schema's descriptions use as {appName}.</summary>
    public static string Clean(string text) => text.Replace("{appName}", "Barbican", StringComparison.Ordinal);

    /// <summary>Runs async work from an event handler: what it throws ends in an error toast, never in the dispatcher.</summary>
    public static void Run(Func<Task> work) => _ = GuardAsync(work);

    public static TextBlock Body(string text) => new() { Text = Clean(text), Style = (Style)Application.Current.Resources["Text.Body"], TextWrapping = TextWrapping.Wrap };

    public static TextBlock Meta(string text) => new() { Text = Clean(text), Style = (Style)Application.Current.Resources["Text.Meta"], TextWrapping = TextWrapping.Wrap };

    public static TextBlock Heading(string text) => new()
    {
        Text = Clean(text),
        FontSize = 15,
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = AppResources.Brush("Text"),
        Margin = new Thickness(0, 18, 0, 2),
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Title(string text) => new() { Text = text, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text") };

    public static Button Pill(string label, Action click, string id, bool primary = false)
    {
        var b = new Button { Content = label, MinWidth = 80, Height = 32, CornerRadius = new CornerRadius(16), FontSize = 13, Style = (Style)Application.Current.Resources[primary ? "Button.Primary" : "Button.Action"] };
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => click();
        return b;
    }

    public static T Id<T>(T element, string id)
        where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, id);
        return element;
    }

    /// <summary>A label and its description on the left, the control on the right.</summary>
    public static Grid RowOf(string label, string description, FrameworkElement? control, string id) =>
        RowWithDescription(label, description.Length > 0 ? Meta(description) : null, control, id);

    internal static Grid RowWithDescription(string label, TextBlock? description, FrameworkElement? control, string id)
    {
        var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 8, 0, 8), BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 2 };
        left.Children.Add(Body(label));
        if (description is not null)
        {
            left.Children.Add(description);
        }

        grid.Children.Add(left);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        AutomationProperties.SetAutomationId(grid, id);
        return grid;
    }

    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    public static StackPanel Page(string title)
    {
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(28, 20, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Title(title));
        return panel;
    }

    private static async Task GuardAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // the data layer has not landed this part: the page stays as it was
        }
#pragma warning disable CA1031 // an event handler must not throw: the failure becomes a toast
        catch (Exception e)
#pragma warning restore CA1031
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Settings: " + e.Message, IsError: true));
        }
    }
}
