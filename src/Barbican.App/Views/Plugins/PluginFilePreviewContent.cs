#if WINDOWS
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginFilePreviewContent : IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 700;

    public bool CanDraw(object request) => request is PluginFilePreviewRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        var request = (PluginFilePreviewRequest)dialog.Request;
        var close = DialogStyles.FooterButton("Close", Barbican.Presentation.Compose.DialogTone.Plain, "plugin.file.close", () => { });
        AutomationProperties.SetAutomationId(close, "plugin.file.close");
        close.Click += (_, _) => dialog.Complete(true);
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(request.Preview);
        content.Children.Add(DialogStyles.Footer(close));
        return content;
    }
}
#endif
