#if AUTOMATION
// Mirrors: spec/automation-protocol.md (feature commands are IAutomationCommand registrations of the feature); M6 plan section 3.3 (set-* commands, owner set-ui). Hidden automation only (ADR 0007): nothing here injects pointer or key input.
//   set-open PAGE [ANCHOR]   opens the Settings app on a page (a SettingsPageIds value) and scrolls to a row
//   set-search TEXT          types in the search box ("-" clears)
//   set-advanced on|off      Show advanced settings
//   set-set KEY VALUE        writes a pref through the settings values (null clears; true, false, a number or text)
//   set-get KEY              reads a pref
//   set-ask TEXT             asks the assistant column
//   set-dump                 the state as one line of JSON

using System.Text.Json.Nodes;
using Barbican.Automation;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0051 // automation command tables read as one list of commands
#pragma warning disable MA0048 // the command record sits beside its table

namespace Barbican.App.Views.Settings.Automation;

/// <summary>One settings automation command.</summary>
public sealed class SetUiCommand(string name, Func<string, CancellationToken, Task<string>> run) : IAutomationCommand
{
    public const int Count = 7;

    public string Name => name;

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        try
        {
            return await run((args ?? string.Empty).Trim(), cancellationToken).ConfigureAwait(true);
        }
        catch (Barbican.Core.Common.PortPendingException e)
        {
            return "error " + e.Message;
        }
    }

    public static IEnumerable<IAutomationCommand> Create(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        var vm = sp.GetRequiredService<ISetUiViewModel>();
        var nav = sp.GetRequiredService<ISettingsNavigation>();
        var shell = sp.GetRequiredService<ShellViewModel>();
        var assistant = sp.GetRequiredService<SetUiAssistantViewModel>();

        yield return new SetUiCommand("set-open", (a, _) =>
        {
            var words = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var page = words.Length > 0 ? words[0] : SettingsPageIds.Account;
            if (SettingsNav.Find(page) is null)
            {
                return Task.FromResult("error no such page " + page);
            }

            if (!shell.TryActivate(AppId.Settings))
            {
                return Task.FromResult("error the Settings app is not available");
            }

            nav.Open(page, words.Length > 1 ? words[1] : null);
            return Task.FromResult("ok");
        });
        yield return new SetUiCommand("set-search", (a, _) =>
        {
            vm.SearchText = string.Equals(a, "-", StringComparison.Ordinal) ? string.Empty : a;
            return Task.FromResult("ok " + vm.SearchHits.Count + " hits");
        });
        yield return new SetUiCommand("set-advanced", (a, _) =>
        {
            vm.ShowAdvanced = string.Equals(a, "on", StringComparison.Ordinal);
            return Task.FromResult("ok");
        });
        yield return new SetUiCommand("set-set", async (a, ct) =>
        {
            var split = a.IndexOf(' ', StringComparison.Ordinal);
            if (split < 0)
            {
                return "error usage: set-set KEY VALUE";
            }

            var raw = a[(split + 1)..].Trim();
            JsonNode? value = string.Equals(raw, "null", StringComparison.Ordinal) ? null : bool.TryParse(raw, out var b) ? JsonValue.Create(b) : double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d) : JsonValue.Create(raw);
            await vm.SetKeyAsync(a[..split], value, ct).ConfigureAwait(true);
            return vm.LastError.Length == 0 ? "ok" : "error " + vm.LastError;
        });
        yield return new SetUiCommand("set-get", (a, _) => Task.FromResult(vm.ValueOf(a)?.ToJsonString() ?? "null"));
        yield return new SetUiCommand("set-ask", async (a, ct) =>
        {
            await assistant.AskAsync(a, null, ct).ConfigureAwait(true);
            return assistant.Error.Length == 0 ? "ok " + assistant.Messages.Count + " messages" : "error " + assistant.Error;
        });
        yield return new SetUiCommand("set-dump", (_, _) =>
        {
            var o = new JsonObject
            {
                ["page"] = nav.Current,
                ["anchor"] = nav.Anchor,
                ["canBack"] = nav.CanGoBack,
                ["canForward"] = nav.CanGoForward,
                ["advanced"] = vm.ShowAdvanced,
                ["search"] = vm.SearchText,
                ["hits"] = new JsonArray([.. vm.SearchHits.Take(10).Select(h => (JsonNode?)JsonValue.Create(h.PageId + ":" + h.RowId))]),
                ["pages"] = new JsonArray([.. vm.VisiblePages.Select(p => (JsonNode?)JsonValue.Create(p.Id))]),
                ["rows"] = new JsonArray([.. vm.RowsOf(nav.Current).Where(r => r.Visible).Select(r => (JsonNode?)JsonValue.Create(r.Row.Id + "=" + (r.Value?.ToJsonString() ?? "")))]),
            };
            return Task.FromResult(o.ToJsonString());
        });
    }
}
#endif
