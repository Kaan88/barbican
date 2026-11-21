// Mirrors: Bulwark components/files/upload-conflict-dialog.tsx:33-60; G-016, D-M5-14. Alert dialog without a close X: Cancel at the start (ghost, sm:me-auto), then Skip, Replace and Keep both; "Apply to all" repeats the answer for the rest of the upload.
// Barbican keeps Keep both as the primary button and the one that starts focused (G-016); Bulwark makes Replace primary (:40,:47). Shell, footer and spacing: ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Views.Dialogs;
using Barbican.Core.Files;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;

namespace Barbican.App.Views.Files;

/// <summary>Draws <see cref="FileConflictDialogRequest"/> in the dialog host.</summary>
public sealed class FileConflictDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is FileConflictDialogRequest;

    public bool OwnButtons => true;

    /// <inheritdoc />
    public Control? InitialFocus(UIElement content) => DialogStyles.TaggedFocus(content);

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var name = ((FileConflictDialogRequest)dialog.Request).Name;
        var all = new CheckBox { Content = "Apply to all" };
        AutomationProperties.SetAutomationId(all, "files.conflict.all");
        Button Choice(string label, FileConflictChoice choice, string id, DialogTone tone) =>
            DialogStyles.FooterButton(label, tone, id, () => dialog.Complete(new FileConflictAnswer(choice, all.IsChecked == true)));

        var keepBoth = Choice("Keep both", FileConflictChoice.KeepBoth, "files.conflict.keep-both", DialogTone.Primary);
        var cancel = DialogStyles.FooterButton("Cancel", DialogTone.Plain, "files.conflict.cancel", () => dialog.Complete(null));
        var panel = new StackPanel
        {
            Spacing = 8,
            MinWidth = 380,
            Tag = keepBoth,
            Children =
            {
                DialogStyles.Description("A file named \"" + name + "\" already exists here."),
                all,
                DialogStyles.Footer(cancel, Choice("Skip", FileConflictChoice.Skip, "files.conflict.skip", DialogTone.Plain), Choice("Replace", FileConflictChoice.Replace, "files.conflict.replace", DialogTone.Plain), keepBoth),
            },
        };
        AutomationProperties.SetAutomationId(panel, "files.conflict");
        return panel;
    }
}
