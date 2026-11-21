// Mirrors: Bulwark components/email/rename-attachment-dialog.tsx:21-43 through components/ui/prompt-dialog.tsx:97-158 (one field and the fixed extension
// beside it, an error line under it while the typed name is invalid, the confirm button off while empty or invalid, Cancel as the outline button) and
// locales/en common.json:1296-1302 (Rename attachment, Rename, "File names can't contain / or \ or control characters", "This name is too long").
// The card, scrim and focus handling are the shared dialog host's; this draws the row, the error and the footer. Owner reader.

using Barbican.App.Theme;
using Barbican.Core.Mail.Attachments;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Reader;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws <see cref="RenameAttachmentDialogRequest"/>: the stem in a field, the extension after it, the error under it.</summary>
public sealed class RenameAttachmentDialogContent : IDialogContent
{
    /// <inheritdoc />
    public bool CanDraw(object request) => request is RenameAttachmentDialogRequest;

    /// <inheritdoc />
    public bool OwnButtons => true;

    /// <inheritdoc />
    public Control? InitialFocus(UIElement content) => DialogStyles.TaggedFocus(content);

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var request = (RenameAttachmentDialogRequest)dialog.Request;
        var field = new TextBox { Text = request.Stem, MinWidth = 260 };
        AutomationProperties.SetName(field, "Name");
        AutomationProperties.SetAutomationId(field, "reader.rename.field");
        var suffix = Suffix(request.Extension);
        var error = Message();
        var cancel = DialogStyles.FooterButton("Cancel", DialogTone.Plain, "reader.rename.cancel", () => dialog.Complete(null));
        var rename = DialogStyles.FooterButton("Rename", DialogTone.Primary, "reader.rename.ok", () => Submit(field.Text, request.Extension, dialog));

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Row(field, suffix));
        panel.Children.Add(error);
        panel.Children.Add(DialogStyles.Footer(cancel, rename));
        panel.Tag = field;
        Bind(field, error, rename, request.Extension, dialog);
        return panel;
    }

    // prompt-dialog.tsx:135-151: the field with the fixed extension after it, read the way the name reads
    private static Grid Row(TextBox field, TextBlock suffix)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(field);
        Grid.SetColumn(suffix, 1);
        row.Children.Add(suffix);
        return row;
    }

    private static TextBlock Suffix(string extension)
    {
        var text = new TextBlock
        {
            Text = extension,
            FontSize = 14,
            Foreground = AppResources.Brush("Dim"),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = extension.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
        };
        AutomationProperties.SetAutomationId(text, "reader.rename.suffix");
        return text;
    }

    // prompt-dialog.tsx:153-157: the reason under the field while the typed name cannot be used
    private static TextBlock Message()
    {
        var text = new TextBlock
        {
            FontSize = 12,
            Foreground = AppResources.Brush("Destructive"),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(text, "reader.rename.error");
        return text;
    }

    // prompt-dialog.tsx:82-95: the check runs as the user types; Enter submits only a usable name, Escape cancels
    private static void Bind(TextBox field, TextBlock error, Button rename, string extension, ActiveDialog dialog)
    {
        void Refresh()
        {
            var typed = field.Text.Trim();
            var problem = typed.Length == 0 ? AttachmentNameProblem.None : AttachmentNames.Problem(typed + extension);
            error.Text = problem switch
            {
                AttachmentNameProblem.TooLong => "This name is too long",
                AttachmentNameProblem.InvalidCharacters => "File names can't contain / or \\ or control characters",
                _ => string.Empty,
            };
            error.Visibility = error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            rename.IsEnabled = typed.Length > 0 && problem == AttachmentNameProblem.None;
        }

        field.TextChanged += (_, _) => Refresh();
        field.KeyDown += (_, e) =>
        {
            // a TextBox keeps its keys away from the shell's router and the host's field handler: Enter and Escape are handled here
            if (e.Key == global::Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Submit(field.Text, extension, dialog);
            }
            else if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                dialog.Complete(null);
            }
        };
        field.Loaded += (_, _) => field.SelectAll();
        Refresh();
    }

    // prompt-dialog.tsx:88-95: the trimmed stem and the fixed extension; nothing goes out while empty or invalid
    private static void Submit(string text, string extension, ActiveDialog dialog)
    {
        var typed = text.Trim();
        if (typed.Length == 0 || AttachmentNames.Problem(typed + extension) != AttachmentNameProblem.None)
        {
            return;
        }

        dialog.Complete(typed + extension);
    }
}
