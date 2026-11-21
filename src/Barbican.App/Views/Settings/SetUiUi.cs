// Mirrors: M6 plan section 6.2 (the settings views); the controls the pages share: text, buttons, rows, the guarded async handler. Every popup is in-window (ADR 0007): dialogs go through IDialogService.
// Bulwark components/settings/settings-section.tsx:222-342 (section: h3 text-lg font-medium + text-sm muted description; row: py-3 border-b, label text-sm font-medium, description text-xs muted mt-1),
// settings-app.tsx:1188-1194 (page max-w-3xl mx-auto px-6 py-6, the h1 is sr-only), ui/button.tsx:15-23 (sm h-9 px-3 text-sm font-medium). Sizes come from Theme/StyleState.Settings.cs; Bento and Nordic keep their own.

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

    /// <summary>Body text: Bulwark text-sm (14 px, globals.css:13-95; TH-05), Bento and Nordic keep 13.</summary>
    public static TextBlock Body(string text) => new() { Text = Clean(text), Style = (Style)Application.Current.Resources["Text.Body"], FontSize = StyleState.Current.SetLabelSize, TextWrapping = TextWrapping.Wrap };

    public static TextBlock Meta(string text) => new() { Text = Clean(text), Style = (Style)Application.Current.Resources["Text.Meta"], TextWrapping = TextWrapping.Wrap };

    /// <summary>A section's title: Bulwark text-lg font-medium with the space-y-8 gap above it, Bento 15 bold.</summary>
    public static TextBlock Heading(string text)
    {
        var look = StyleState.Current;
        return new TextBlock
        {
            Text = Clean(text),
            FontSize = look.SetSectionSize,
            FontWeight = look.SetSectionWeight,
            Foreground = AppResources.Brush("Text"),
            Margin = new Thickness(0, look.SetSectionGap, 0, 2),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    /// <summary>A row's label: Bulwark text-sm font-medium in the foreground colour.</summary>
    public static TextBlock Label(string text)
    {
        var look = StyleState.Current;
        return new TextBlock { Text = Clean(text), FontSize = look.SetLabelSize, FontWeight = look.SetLabelWeight, Foreground = AppResources.Brush(look.SetLabelBrush), TextWrapping = TextWrapping.Wrap };
    }

    public static TextBlock Title(string text) => new() { Text = text, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text") };

    /// <summary>A button of a row: Bulwark's h-9 px-3 text-sm font-medium with the shape radius, or the older pill.</summary>
    public static Button Pill(string label, Action click, string id, bool primary = false)
    {
        var look = StyleState.Current;
        var b = new Button
        {
            Content = label,
            MinWidth = look.SetButtonMinWidth,
            Height = look.SetButtonHeight,
            CornerRadius = look.SetButtonRadius,
            FontSize = look.SetButtonSize,
            FontWeight = look.SetButtonWeight,
            Padding = look.SetButtonPadding,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources[primary ? "Button.Primary" : "Button.Action"],
        };
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
        var look = StyleState.Current;
        var grid = new Grid { ColumnSpacing = 16, Padding = look.SetRowPadding, BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = look.SetDescGap };
        left.Children.Add(Label(label));
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

    public static ScrollViewer Scroll(UIElement content)
    {
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (content is FrameworkElement { Tag: Action fit })
        {
            scroll.SizeChanged += (_, _) => fit();
        }

        return scroll;
    }

    /// <summary>
    /// Sets the width of a page column to its widest line, or to the viewport when that is narrower. A column that is only capped by MaxWidth is arranged at the width
    /// of its widest child when that child ignores the cap, which pushes a centred column off the middle.
    /// </summary>
    internal static void FitColumn(FrameworkElement column, bool bulwark, double max, double inset)
    {
        ArgumentNullException.ThrowIfNull(column);
        var viewport = (column.Parent as FrameworkElement)?.ActualWidth ?? 0;
        if (!bulwark || viewport <= 0)
        {
            column.ClearValue(FrameworkElement.WidthProperty);
            return;
        }

        column.Width = Math.Max(0, Math.Min(max, viewport - inset));
    }

    /// <summary>The body of a page: Bulwark's centred max-w-3xl column with no visible title (the nav names the page), Bento's titled left column. Follows a style switch.</summary>
    public static StackPanel Page(string title)
    {
        var look = StyleState.Current;
        var panel = new StackPanel { Spacing = 4 };
        var heading = Title(title);
        panel.Children.Add(heading);
        AutomationProperties.SetName(panel, title);
        void Fit() => FitColumn(panel, look.IsBulwark, look.SetPageMaxWidth, 0);
        panel.Tag = (Action)Fit;
        void Apply()
        {
            Fit();
            panel.Padding = look.SetPagePadding;
            panel.MaxWidth = look.SetPageMaxWidth;
            panel.HorizontalAlignment = look.SetPageAlignment;
            heading.Visibility = look.SetPageTitleVisibility;
        }

        Apply();
        var style = look.Style;
        look.PropertyChanged += (_, _) =>
        {
            if (look.Style == style)
            {
                return;
            }

            style = look.Style;
            Apply();
        };
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
