// Mirrors: Bulwark components/files/move-dialog.tsx: My files and every valid folder (not inside the moved items) as an indented list; a click answers. Footer: Cancel, folder-picker-dialog.tsx:146-159; shell ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Views.Calendar;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;

namespace Barbican.App.Views.Files;

/// <summary>Draws <see cref="FileMoveDialogRequest"/> in the dialog host.</summary>
public sealed class FileMoveDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is FileMoveDialogRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var request = (FileMoveDialogRequest)dialog.Request;
        var list = new StackPanel { Spacing = 2 };
        Button Row(string text, int depth, string? key, string id)
        {
            var b = new Button
            {
                Style = (Style)Application.Current.Resources["Button.Action"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(depth * 16, 0, 0, 0), Children = { CalendarVisuals.Icon("Folder", 16), new TextBlock { Text = text } } },
            };
            AutomationProperties.SetAutomationId(b, id);
            b.Click += (_, _) => dialog.Complete(new FileMoveAnswer(key));
            return b;
        }

        list.Children.Add(Row("My files", 0, null, "files.move.root"));
        foreach (var t in request.Folders)
        {
            list.Children.Add(Row(t.Folder.Name, t.Depth + 1, t.Folder.Key, "files.move." + t.Folder.Key));
        }

        var cancel = DialogStyles.FooterButton("Cancel", Barbican.Presentation.Compose.DialogTone.Plain, "files.move.cancel", () => dialog.Complete(null));
        return new StackPanel { Spacing = 8, MinWidth = 340, Children = { new ScrollViewer { Content = list, MaxHeight = 320 }, DialogStyles.Footer(cancel) } };
    }
}
