// Mirrors: Bulwark components/email/view-source-dialog.tsx (monospace source, Copy, download); Rust reader.rs source banner (Copy source, Save .eml, the cut note). The card of the
// dialog host holds it (IDialogContent); the host's OK closes. Owner reader. I-183.

using Barbican.App.Theme;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Reader;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws <see cref="SourceDialogRequest"/>: a scrolling read-only block of the raw message and two buttons.</summary>
public sealed class SourceDialogContent : IDialogContent
{
    /// <inheritdoc />
    public bool CanDraw(object request) => request is SourceDialogRequest;

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var request = (SourceDialogRequest)dialog.Request;
        var text = new TextBox
        {
            Text = request.Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Height = 280,
            MinWidth = 380,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(text, "source.text");
        AutomationProperties.SetName(text, "Message source");

        var copy = Button("Copy source", "source.copy", () => request.Copy(request.Text));
        var save = Button("Save .eml", "source.save", () => _ = request.SaveEml(CancellationToken.None));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(copy);
        buttons.Children.Add(save);

        var panel = new StackPanel { Spacing = 10 };
        if (request.Cut)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "The source is cut here. Save .eml saves the whole message.",
                FontSize = 12,
                Foreground = AppResources.Brush("Faint"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        panel.Children.Add(text);
        panel.Children.Add(buttons);
        return panel;
    }

    private static Button Button(string label, string id, Action click)
    {
        var button = new Button { Content = label, Style = (Style)Application.Current.Resources["Button.Action"] };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }
}
