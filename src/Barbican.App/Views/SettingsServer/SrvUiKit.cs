// Bulwark look (WP-11, ST-10): sections text-lg font-medium with space-y-8 above (settings-section.tsx:222-240), a card is a bordered rounded-lg p-3 with no fill (plugins-settings.tsx:196-199), small buttons h-8 text-sm
// font-medium with the shape radius (ui/button.tsx:15-23), a choice is the bg-muted bordered select (settings-section.tsx:432-460); Bento and Nordic keep the earlier look.
// Mirrors: ADR 0007 (in-window only: no ComboBox drop-down, no flyout, no date picker; choices are chips or a stepping button, dates are typed), M6 plan D-M6-00 (a view touches controls on the UI thread only). Small builders and helpers shared by the srv-ui pages. Owner srv-ui.

#pragma warning disable CA1859 // the builders return UIElement by design

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.SettingsServer;

/// <summary>Builders and helpers of the server-backed settings pages.</summary>
internal static class SrvUiKit
{
    public static TextBlock Title(string text) => new() { Text = text, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text") };

    public static TextBlock Section(string text)
    {
        var look = StyleState.Current;
        return look.IsBulwark
            ? new TextBlock { Text = text, FontSize = look.SetSectionSize, FontWeight = look.SetSectionWeight, Foreground = AppResources.Brush("Text"), Margin = new Thickness(0, look.SetSectionGap, 0, 0) }
            : new TextBlock { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text"), Margin = new Thickness(0, 10, 0, 0) };
    }

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
        var look = StyleState.Current;
        var card = look.IsBulwark
            ? new Border { BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = look.SetCardRadius, Padding = new Thickness(12), Child = content }
            : new Border { Background = AppResources.Brush("Raised"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Child = content };
        if (id is not null)
        {
            AutomationProperties.SetAutomationId(card, id);
        }

        return card;
    }

    /// <summary>A small button for a list row.</summary>
    public static Button Small(string label, string id, Action click, bool danger = false)
    {
        var look = StyleState.Current;
        var button = new Button
        {
            Content = label,
            Height = look.IsBulwark ? 32 : 28,
            FontSize = look.IsBulwark ? 14 : 12,
            FontWeight = look.IsBulwark ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.SemiBold,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = look.IsBulwark ? look.ButtonRadius : new CornerRadius(14),
            Style = (Style)Application.Current.Resources["Button.Action"],
        };
        if (danger)
        {
            button.Foreground = AppResources.Brush("RoseFg");
        }

        button.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>A footer button of a settings dialog: the shared dialog button look (Bulwark h-10 with the shape radius, ui/button.tsx:42-56; the older pill otherwise), MN-03.</summary>
    public static Button Foot(string label, DialogTone tone, string id, Action click, bool enabled = true)
    {
        var button = Calendar.Editors.CalendarCardKit.Btn(label, tone, id, click, enabled);
        Dialogs.DialogStyles.Dress(button);
        return button;
    }

    /// <summary>A one-of-many choice as one button that steps to the next choice on a click (no drop-down: ADR 0007); the label shows the current one.</summary>
    public static Button Cycle(string id, IReadOnlyList<string> labels, int current, Action<int> set)
    {
        var at = Math.Clamp(current, 0, Math.Max(0, labels.Count - 1));
        var look = StyleState.Current;
        var button = new Button { Height = look.IsBulwark ? 36 : 30, FontSize = look.IsBulwark ? 14 : 12.5, Padding = new Thickness(12, 0, 12, 0), CornerRadius = look.IsBulwark ? look.ButtonRadius : new CornerRadius(15), Style = (Style)Application.Current.Resources["Button.Action"], MinWidth = 96 };
        if (look.IsBulwark)
        {
            button.Background = AppResources.Brush("Input");
        }

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
