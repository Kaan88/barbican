#if AUTOMATION
// Mirrors: spec/automation-protocol.md (hidden, in-memory only). Opens one of the content dialogs on demand so a capture can compare it with Bulwark's dialog shell (MN-03): `dialog-probe conflict|apply|cal-confirm|cal-scope|app calendar`.

using Barbican.Automation;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Shell.Automation;

/// <summary>The <c>dialog-probe</c> command.</summary>
public sealed class DialogProbeCommand(IServiceProvider services) : IAutomationCommand
{
    public string Name => "dialog-probe";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var dialogs = services.GetRequiredService<DialogService>();
        switch ((args ?? string.Empty).Trim())
        {
            case "conflict":
                _ = dialogs.ShowAsync(new FileConflictDialogRequest("report.pdf"), CancellationToken.None);
                return "ok";
            case "apply":
                var page = services.GetRequiredService<ISrvUiFiltersViewModel>();
                await page.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                var row = page.Rows.Count > 0 ? page.Rows[0] : null;
                var rule = row is null ? null : page.RuleOf(row.Id);
                if (rule is null)
                {
                    return "error no rule";
                }

                _ = dialogs.ShowAsync(new SrvUiFilterApplyRequest(new SrvUiFilterApplyModel(page, rule)), CancellationToken.None);
                return "ok";
            case "cal-confirm":
                _ = services.GetRequiredService<CalendarEditorHost>().ConfirmAsync("delete-task", "Delete task?", "\"Team stand-up\" will be deleted.", "Delete", true, CancellationToken.None);
                return "ok";
            case "cal-scope":
                _ = services.GetRequiredService<CalendarEditorHost>().AskScopeAsync("Team stand-up", true, false, CancellationToken.None);
                return "ok";
            case "app calendar":
                return services.GetRequiredService<ShellViewModel>().TryActivate(AppId.Calendar) ? "ok" : "error";
            case "app mail":
                return services.GetRequiredService<ShellViewModel>().TryActivate(AppId.Mail) ? "ok" : "error";
            default:
                return "error usage: dialog-probe conflict|apply|cal-confirm|cal-scope|app calendar|app mail";
        }
    }
}
#endif
