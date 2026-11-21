// Mirrors: Bulwark components/email/send-warnings-dialog.tsx:121-150 (alertdialog, no close X, bullet list and placeholder chips under the text, Back then Send anyway), external-recipient-confirm.tsx:203-250
// (warning icon, count text, bordered To/Cc/Bcc list, Back first so the focus starts there, Escape answers no) and the close dialog of email-composer.tsx:6854-6879 (Cancel, Discard destructive, Save primary);
// ui/dialog.tsx:131-134 for the title, text and footer spacing. CP-07. The composer's questions live in the shared dialog host: the card, scrim, PopIn and focus handling are its own.
// Every question answers with the id of a choice, or null when dismissed: the composer treats anything but the go-ahead id as "not sent" (see ComposerViewModel.Send.cs).

using Barbican.App.Controls;
using Barbican.App.Theme;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws <see cref="ComposerQuestionRequest"/>: the text, the lists and the buttons of a <see cref="ComposerDialog"/>.</summary>
public sealed class ComposerQuestionContent : IDialogContent
{
    /// <inheritdoc />
    public bool CanDraw(object request) => request is ComposerQuestionRequest;

    /// <inheritdoc />
    public bool OwnButtons => true;

    /// <inheritdoc />
    public Control? InitialFocus(UIElement content) =>
        content is FrameworkElement { Tag: StackPanel footer } ? FocusManager.FindFirstFocusableElement(footer) as Control : null;

    /// <inheritdoc />
    public DialogIcon? HeaderIcon(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return dialog.Request is ComposerQuestionRequest { Question.Id: "external" } ? new DialogIcon("Building", "Warning", "AmberBg") : null;
    }

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var question = ((ComposerQuestionRequest)dialog.Request).Question;
        var panel = new StackPanel { Spacing = 12 };
        if (question.Message.Length > 0)
        {
            // dialog.tsx:132 mt-2 under the title: the host's column gap is 12, this is 8
            panel.Children.Add(new TextBlock { Text = question.Message, FontSize = 14, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -4, 0, 0) });
        }

        if (string.Equals(question.Id, "external", StringComparison.Ordinal))
        {
            panel.Children.Add(Recipients(question.Lines));
        }
        else
        {
            foreach (var line in question.Lines)
            {
                panel.Children.Add(Bullet(line));
            }
        }

        if (question.Chips.Count > 0)
        {
            panel.Children.Add(Chips(question.Chips));
        }

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = DialogStyles.ButtonGap, Margin = new Thickness(0, DialogStyles.Bulwark ? 12 : 0, 0, 0) };
        foreach (var choice in Ordered(question.Choices))
        {
            var id = choice.Id;
            footer.Children.Add(DialogStyles.FooterButton(choice.Label, choice.Tone, "composer.dialog." + id, () => dialog.Complete(id)));
        }

        panel.Children.Add(footer);
        panel.Tag = footer;
        AutomationProperties.SetAutomationId(panel, "composer.dialog");
        AutomationProperties.SetName(panel, question.Title);
        // Escape answers "no" (external-recipient-confirm.tsx:186-192); the key does not go on to the shell behind the dialog
        panel.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                dialog.Complete(null);
            }
        };
        return panel;
    }

    /// <summary>The footer order of Bulwark's dialogs: the safe button first (outline), a destructive one next, the primary one last.</summary>
    internal static IEnumerable<DialogChoice> Ordered(IReadOnlyList<DialogChoice> choices) =>
        choices.OrderBy(c => c.Tone switch { DialogTone.Plain => 0, DialogTone.Danger => 1, _ => 2 });

    private static Grid Bullet(string text)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = "•", FontSize = 14, Foreground = AppResources.Brush("Dim") });
        var label = new TextBlock { Text = text, FontSize = 14, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(label, "composer.dialog.line");
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    // send-warnings-dialog.tsx:50-61: up to five chips (rounded bg-muted, mono text-xs) and "and N more"
    private static WrapRow Chips(IReadOnlyList<string> chips)
    {
        var wrap = new WrapRow { Spacing = 6 };
        foreach (var chip in chips.Take(5))
        {
            var label = new TextBlock { Text = chip, FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = AppResources.Brush("Text"), MaxWidth = 380, TextTrimming = TextTrimming.CharacterEllipsis };
            AutomationProperties.SetAutomationId(label, "composer.dialog.chip");
            wrap.Children.Add(new Border { Background = AppResources.Brush("Chip"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Child = label });
        }

        if (chips.Count > 5)
        {
            wrap.Children.Add(new TextBlock { Text = $"and {chips.Count - 5} more", FontSize = 12, Foreground = AppResources.Brush("Dim"), VerticalAlignment = VerticalAlignment.Center });
        }

        return wrap;
    }

    // external-recipient-confirm.tsx:233-240: a bordered box, one row per address: the field in a 32 px muted column, then the address
    private static Border Recipients(IReadOnlyList<string> lines)
    {
        var list = new StackPanel { Spacing = 4 };
        foreach (var line in lines)
        {
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            var (field, address) = split > 0 ? (line[..split], line[(split + 2)..]) : (string.Empty, line);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = field, FontSize = 12, Foreground = AppResources.Brush("Dim"), VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBlock { Text = address, FontSize = 14, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAutomationId(text, "composer.dialog.line");
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(row);
        }

        var scroll = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return new Border { BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Child = scroll };
    }
}
