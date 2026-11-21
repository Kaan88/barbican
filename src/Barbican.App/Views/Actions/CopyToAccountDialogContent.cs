// Mirrors: Bulwark components/email/email-context-menu.tsx:371-379 (the "Copy to account" list: a heading per account above its folders, indented)
// and hooks/use-message-file-actions.ts:181-195; a click answers with the folder, Cancel answers null. The dialog stands in for Bulwark's submenu.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Theme;
using Barbican.App.Views.Calendar;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Actions;

/// <summary>Draws <see cref="CopyToAccountDialogRequest"/> in the dialog host.</summary>
public sealed class CopyToAccountDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is CopyToAccountDialogRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var request = (CopyToAccountDialogRequest)dialog.Request;
        var list = new StackPanel { Spacing = 2 };
        foreach (var account in request.Accounts)
        {
            list.Children.Add(new TextBlock
            {
                Text = account.Label,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = AppResources.Brush("Faint"),
                Margin = new Thickness(10, list.Children.Count == 0 ? 0 : 8, 10, 2),
            });
            foreach (var folder in account.Folders)
            {
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["Button.Action"],
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(folder.Depth * 16, 0, 0, 0), Children = { CalendarVisuals.Icon("Folder", 16), new TextBlock { Text = folder.Name } } },
                };
                AutomationProperties.SetAutomationId(button, "mail.copy." + folder.MailboxId);
                button.Click += (_, _) => dialog.Complete(new CopyToAccountAnswer(account.AccountKey, folder.MailboxId));
                list.Children.Add(button);
            }
        }

        var cancel = DialogStyles.FooterButton("Cancel", DialogTone.Plain, "mail.copy.cancel", () => dialog.Complete(null));
        return new StackPanel { Spacing = 8, MinWidth = 340, Children = { new ScrollViewer { Content = list, MaxHeight = 320 }, DialogStyles.Footer(cancel) } };
    }
}
