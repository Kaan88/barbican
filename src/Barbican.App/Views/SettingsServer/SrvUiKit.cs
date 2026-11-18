// Mirrors: ADR 0007 (in-window only: no ComboBox drop-down, no flyout, no date picker; choices are chips or a stepping button, dates are typed), M6 plan D-M6-00 (a view touches controls on the UI thread only). Small builders and helpers shared by the srv-ui pages. Owner srv-ui.

#pragma warning disable CA1859 // the builders return UIElement by design

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.SettingsServer;

/// <summary>Builders and helpers of the server-backed settings pages.</summary>
internal static class SrvUiKit
{
    public static TextBlock Title(string text) => new() { Text = text, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text") };

    public static TextBlock Section(string text) => new() { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text"), Margin = new Thickness(0, 10, 0, 0) };

    public static TextBlock Dim(string text) => new() { Text = text, FontSize = 12.5, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Strong(string text) => new() { Text = text, FontSize = 13.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Error(string? text)
    {
        var block = new TextBlock { Text = text ?? string.Empty, FontSize = 12.5, Foreground = AppResources.Brush("RoseFg"), TextWrapping = TextWrapping.Wrap };
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        return block;
    }

    /// <summary>A raised block that groups a row or a form.</summary>
    public static Border Card(UIElement content, string? id = null)
    {
        var card = new Border { Background = AppResources.Brush("Raised"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Child = content };
        if (id is not null)
        {
            AutomationProperties.SetAutomationId(card, id);
        }

        return card;
    }

    /// <summary>A small button for a list row.</summary>
    public static Button Small(string label, string id, Action click, bool danger = false)
    {
        var button = new Button { Content = label, Height = 28, FontSize = 12, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(14), Style = (Style)Application.Current.Resources["Button.Action"] };
        if (danger)
        {
            button.Foreground = AppResources.Brush("RoseFg");
        }

        button.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>A one-of-many choice as one button that steps to the next choice on a click (no drop-down: ADR 0007); the label shows the current one.</summary>
    public static Button Cycle(string id, IReadOnlyList<string> labels, int current, Action<int> set)
    {
        var at = Math.Clamp(current, 0, Math.Max(0, labels.Count - 1));
        var button = new Button { Height = 30, FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), Style = (Style)Application.Current.Resources["Button.Action"], MinWidth = 96 };
        void Paint()
        {
            var text = labels.Count == 0 ? string.Empty : labels[at];
            button.Content = text + "  ▾";
            AutomationProperties.SetName(button, text);
        }

        button.Click += (_, _) =>
        {
            if (labels.Count == 0)
            {
                return;
            }

            at = (at + 1) % labels.Count;
            Paint();
            set(at);
        };
        Paint();
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in children)
        {
            row.Children.Add(c);
        }

        return row;
    }

    /// <summary>Runs async work from an event handler: what it throws ends in an error toast, never in the dispatcher.</summary>
    public static void Run(Func<Task> work) => _ = GuardAsync(work);

    /// <summary>Asks before something permanent happens (D-7); true when confirmed.</summary>
    public static async Task<bool> AskAsync(string title, string message, string confirm)
    {
        var dialogs = ViewHost.Find<IDialogService>();
        if (dialogs is null)
        {
            return false;
        }

        return await dialogs.ShowAsync(new ConfirmDialogRequest(title, message, confirm, Danger: true), CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Asks for one line of text; null when cancelled.</summary>
    public static async Task<string?> PromptAsync(string title, string label, string initial, string confirm)
    {
        var dialogs = ViewHost.Find<IDialogService>();
        return dialogs is null ? null : await dialogs.ShowAsync(new PromptDialogRequest(title, label, initial, confirm), CancellationToken.None).ConfigureAwait(true);
    }

    private static async Task GuardAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // a seam of another stream (the data layer) that has not landed: the page stays as it was
        }
#pragma warning disable CA1031 // an event handler must not throw: the failure becomes a toast
        catch (Exception e)
#pragma warning restore CA1031
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Settings: " + e.Message, IsError: true));
        }
    }
}
