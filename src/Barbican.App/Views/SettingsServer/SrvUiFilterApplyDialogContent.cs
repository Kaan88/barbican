// Mirrors: Rust filters_x.rs run_rule :566 (scan, count, confirm, apply, result text), Bulwark filter-settings.tsx "Apply to existing mail"; T-097..T-099. Draws SrvUiFilterApplyRequest. Owner srv-ui.

using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings.Server;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.SettingsServer;

/// <summary>Draws <see cref="SrvUiFilterApplyRequest"/>.</summary>
public sealed class SrvUiFilterApplyDialogContent : IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 480;

    public bool CanDraw(object request) => request is SrvUiFilterApplyRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((SrvUiFilterApplyRequest)dialog.Request).Model;
        var host = new StackPanel { Spacing = 8, MinWidth = 400 };
        AutomationProperties.SetAutomationId(host, "srv.rule-apply");
        AutomationProperties.SetName(host, "Apply to existing mail");
        m.Closed += (_, _) => dialog.Complete(true);
        m.Changed += (_, _) => host.DispatcherQueue.TryEnqueue(() => Fill(host, m));
        Fill(host, m);
        SrvUiKit.Run(() => m.StartAsync(CancellationToken.None));
        return host;
    }

    private static void Fill(StackPanel host, SrvUiFilterApplyModel m)
    {
        host.Children.Clear();
        host.Children.Add(SrvUiKit.Strong(m.RuleName.Length == 0 ? "Rule" : m.RuleName));
        var text = SrvUiKit.Dim(m.Text);
        AutomationProperties.SetAutomationId(text, "srv.rule-apply.text");
        host.Children.Add(text);
        if (m.Phase is SrvUiApplyPhase.Scanning or SrvUiApplyPhase.Applying)
        {
            host.Children.Add(new ProgressBar { IsIndeterminate = true, MinWidth = 300 });
        }

        host.Children.Add(Buttons(
            m.Phase == SrvUiApplyPhase.Ready
                ? Btn("Apply", DialogTone.Primary, "srv.rule-apply.confirm", () => SrvUiKit.Run(() => m.ApplyAsync(CancellationToken.None)))
                : Btn("Close", DialogTone.Primary, "srv.rule-apply.close", m.Close),
            m.Phase == SrvUiApplyPhase.Ready ? Btn("Cancel", DialogTone.Plain, "srv.rule-apply.cancel", m.Close) : new Border()));
    }
}
