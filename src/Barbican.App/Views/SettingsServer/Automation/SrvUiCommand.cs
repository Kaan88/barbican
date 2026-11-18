#if AUTOMATION
// Mirrors: spec/automation-protocol.md (feature commands are IAutomationCommand registrations of the feature); M6 plan section 3.3 (srv-* commands of srv-ui).
//   srv-open PAGE [ANCHOR]   opens the Settings app on a page (identities, templates, organize, filters, vacation, recover)
//   srv-dump PAGE            the state of a page as one line of JSON (rows, status, error)
// The clicks themselves are the generic `invoke ID` and `text ID VALUE` on the srv.* automation ids.

using System.Text.Json.Nodes;
using Barbican.Automation;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0048 // automation command tables read as one list of commands

namespace Barbican.App.Views.SettingsServer.Automation;

/// <summary>One srv-ui automation command.</summary>
public sealed class SrvUiCommand(string name, Func<string[], Task<string>> run) : IAutomationCommand
{
    public const int Count = 2;

    public string Name => name;

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var words = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            return await run(words).ConfigureAwait(true);
        }
        catch (Barbican.Core.Common.PortPendingException e)
        {
            return "error " + e.Message;
        }
    }

    #pragma warning disable MA0051 // Keep the sample page dumps together as the automation command table.
    public static IEnumerable<IAutomationCommand> Create(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        yield return new SrvUiCommand("srv-open", w =>
        {
            if (w.Length == 0 || SettingsNav.Find(w[0]) is null)
            {
                return Task.FromResult("error usage: srv-open PAGE [ANCHOR]");
            }

            if (!sp.GetRequiredService<ShellViewModel>().TryActivate(AppId.Settings))
            {
                return Task.FromResult("error the Settings app is not available");
            }

            sp.GetRequiredService<ISettingsNavigation>().Open(w[0], w.Length > 1 ? w[1] : null);
            return Task.FromResult("ok");
        });
        yield return new SrvUiCommand("srv-dump", async w =>
        {
            var page = w.Length > 0 ? w[0] : string.Empty;
            var o = new JsonObject { ["page"] = page };
            switch (page)
            {
                case SettingsPageIds.Filters:
                    var f = sp.GetRequiredService<ISrvUiFiltersViewModel>();
                    await f.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                    o["rows"] = new JsonArray([.. f.Rows.Select(r => (JsonNode)JsonValue.Create(r.Name + (r.Enabled ? string.Empty : " (off)"))!)]);
                    o["locked"] = f.Locked;
                    o["error"] = f.LoadError ?? f.SaveError;
                    break;
                case SettingsPageIds.Vacation:
                    var v = sp.GetRequiredService<SrvUiVacationViewModel>();
                    o["status"] = v.Status;
                    o["subject"] = v.Subject;
                    o["error"] = v.Error;
                    break;
                case SettingsPageIds.Identities:
                    var i = sp.GetRequiredService<SrvUiIdentitiesViewModel>();
                    o["items"] = new JsonArray([.. i.Items.Select(x => (JsonNode)JsonValue.Create(x.Email)!)]);
                    o["selected"] = i.SelectedId;
                    o["error"] = i.Error;
                    break;
                case SettingsPageIds.Templates:
                    var t = sp.GetRequiredService<SrvUiTemplatesViewModel>();
                    o["items"] = new JsonArray([.. t.Items.Select(x => (JsonNode)JsonValue.Create(x.Name)!)]);
                    o["selected"] = t.SelectedId;
                    o["error"] = t.Error;
                    break;
                case SettingsPageIds.Organize:
                    var folders = sp.GetRequiredService<SrvUiFoldersViewModel>();
                    o["folders"] = new JsonArray([.. folders.Rows.Select(x => (JsonNode)JsonValue.Create(new string(' ', x.Depth * 2) + x.Node.Name)!)]);
                    o["selected"] = folders.SelectedId;
                    o["tags"] = new JsonArray([.. sp.GetRequiredService<SrvUiTagsViewModel>().Rows.Select(x => (JsonNode)JsonValue.Create(x.Def.Label)!)]);
                    o["error"] = folders.Error;
                    break;
                case SettingsPageIds.Recover:
                    var r = sp.GetRequiredService<SrvUiRecoverViewModel>();
                    o["available"] = r.Available;
                    o["items"] = new JsonArray([.. r.Items.Select(x => (JsonNode)JsonValue.Create(x.Subject)!)]);
                    o["error"] = r.Error;
                    break;
                default:
                    return "error usage: srv-dump PAGE";
            }

            return o.ToJsonString();
        });
    }
#pragma warning restore MA0051
}
#endif
