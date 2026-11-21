// Mirrors: Bulwark mail-app.tsx:7298 (the mailto account picker); I-283. Draws AccountPickRequest inside the card of the dialog host (IDialogContent): one button per account, the active one first; a click answers with the account key.

using Barbican.App.Theme;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Dialogs;

/// <summary>The account question of a mailto link.</summary>
public sealed class AccountPickDialogContent : IDialogContent
{
    /// <inheritdoc />
    public bool CanDraw(object request) => request is AccountPickRequest;

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var request = (AccountPickRequest)dialog.Request;
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = request.Detail, FontSize = 13, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap });
        foreach (var account in request.Accounts.OrderBy(a => string.Equals(a.Key, request.Preferred, StringComparison.Ordinal) ? 0 : 1))
        {
            var key = account.Key;
            var button = new Button
            {
                Style = (Style)Application.Current.Resources[string.Equals(key, request.Preferred, StringComparison.Ordinal) ? "Button.Primary" : "Button.Action"],
                Content = account.User,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 38,
                CornerRadius = ComposerShape.Row(12),
                FontSize = 13,
            };
            AutomationProperties.SetAutomationId(button, "mailto.account." + account.User);
            AutomationProperties.SetName(button, account.User);
            button.Click += (_, _) => dialog.Complete(key);
            panel.Children.Add(button);
        }

        AutomationProperties.SetAutomationId(panel, "mailto.accounts");
        return panel;
    }
}
