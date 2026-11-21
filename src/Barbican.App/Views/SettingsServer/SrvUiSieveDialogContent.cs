// Mirrors: Bulwark filter-settings.tsx (the generated Sieve script shown read-only), B-SIEVE-007. Draws SrvUiSieveRequest: the script in a monospace box nobody can type in. Owner srv-ui. Dialog shell and footer: ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles.

using Barbican.App.Theme;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings.Server;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.SettingsServer;

/// <summary>Draws <see cref="SrvUiSieveRequest"/>.</summary>
public sealed class SrvUiSieveDialogContent : IDialogContent
{
    public double MaxWidth => 720;

    public bool CanDraw(object request) => request is SrvUiSieveRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var script = ((SrvUiSieveRequest)dialog.Request).Script;
        var box = new TextBox
        {
            Text = script.Length == 0 ? "# No rules yet." : script,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            MinWidth = 600,
            MinHeight = 260,
            MaxHeight = 420,
            Foreground = AppResources.Brush("Text"),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(box, "srv.sieve.text");
        AutomationProperties.SetName(box, "Sieve script");
        return box;
    }
}
