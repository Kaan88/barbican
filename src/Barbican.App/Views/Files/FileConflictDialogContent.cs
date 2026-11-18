// Mirrors: Bulwark components/files/upload-conflict-dialog.tsx; G-016, D-M5-14: Keep both is the default (the primary button), Replace and Skip are the others, "Apply to all" repeats the answer for the rest of the upload.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Views.Dialogs;
using Barbican.Core.Files;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;

namespace Barbican.App.Views.Files;

/// <summary>Draws <see cref="FileConflictDialogRequest"/> in the dialog host.</summary>
public sealed class FileConflictDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is FileConflictDialogRequest;

    public bool OwnButtons => true;

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var name = ((FileConflictDialogRequest)dialog.Request).Name;
        var all = new CheckBox { Content = "Apply to all" };
        AutomationProperties.SetAutomationId(all, "files.conflict.all");
        Button Choice(string label, FileConflictChoice choice, string id, bool primary)
        {
            // the same pill the calendar and contacts dialogs use (a primary Button without a height was a flat bar, D-M5-I04)
            var b = new Button { Content = label, MinWidth = 96, Height = 34, CornerRadius = new CornerRadius(17), FontSize = 13, Style = (Style)Application.Current.Resources[primary ? "Button.Primary" : "Button.Action"] };
            AutomationProperties.SetAutomationId(b, id);
            b.Click += (_, _) => dialog.Complete(new FileConflictAnswer(choice, all.IsChecked == true));
            return b;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Choice("Skip", FileConflictChoice.Skip, "files.conflict.skip", false));
        buttons.Children.Add(Choice("Replace", FileConflictChoice.Replace, "files.conflict.replace", false));
        buttons.Children.Add(Choice("Keep both", FileConflictChoice.KeepBoth, "files.conflict.keep-both", true));
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Height = 34, CornerRadius = new CornerRadius(17), FontSize = 13, Style = (Style)Application.Current.Resources["Button.Action"] };
        AutomationProperties.SetAutomationId(cancel, "files.conflict.cancel");
        cancel.Click += (_, _) => dialog.Complete(null);
        buttons.Children.Insert(0, cancel);
        return new StackPanel
        {
            Spacing = 14,
            MinWidth = 380,
            Children =
            {
                new TextBlock { Text = "A file named \"" + name + "\" already exists here.", TextWrapping = TextWrapping.Wrap },
                all,
                buttons,
            },
        };
    }
}
