#if AUTOMATION
// Mirrors: spec/automation-protocol.md (feature commands are IAutomationCommand registrations of the feature); M5 plan section 3.1 (files-open, files-nav, files-select, files-dump, files-drop-sim; the plan names a CommandRouter.Files.cs, the feature-command pattern of the calendar is used instead, D-M5-D07). Hidden automation only (ADR 0007).
//   files-open [place]            opens the Files app (and a place: root, favourites, recent, shared, trash, or a folder key)
//   files-nav PLACE               navigates
//   files-select KEY...           selects (several keys: a multi-selection); "none" clears
//   files-do COMMAND [KEYS]       runs a files.* command by id on the keys (comma separated; the selection when none)
//   files-drop-sim PATH...        the OS file drop onto the list: uploads the paths (the Explorer drag itself is unproven)
//   files-pick FILE...|folder P   queues the answer of the next upload picker
//   files-preview KEY             opens the preview of a file
//   files-dump [state]            the state as one line of JSON

using System.Text.Json.Nodes;
using Barbican.Automation;
using Barbican.Core.Files;
using Barbican.Presentation.Files;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0051 // automation command tables read as one list of commands
#pragma warning disable MA0048 // the command record sits beside its table

namespace Barbican.App.Views.Files.Automation;

/// <summary>One Files automation command.</summary>
public sealed class FilesCommand(string name, Func<string[], CancellationToken, Task<string>> run) : IAutomationCommand
{
    public string Name => name;

    public const int Count = 7;

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var words = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            return await run(words, cancellationToken).ConfigureAwait(true);
        }
        catch (Barbican.Core.Common.PortPendingException e)
        {
            return "error " + e.Message;
        }
    }

    public static IEnumerable<IAutomationCommand> Create(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        var vm = sp.GetRequiredService<FilesViewModel>();
        var actions = sp.GetRequiredService<FilesActions>();
        var shell = sp.GetRequiredService<ShellViewModel>();
        var picker = sp.GetRequiredService<FilesPicker>();

        static string? Place(string w) => w switch
        {
            "root" => null,
            "favourites" => FilesViewModel.FavouritesLocation,
            "recent" => FilesViewModel.RecentLocation,
            "shared" => FilesViewModel.SharedLocation,
            "trash" => FileTrash.TrashLocation,
            _ => w,
        };

        yield return new FilesCommand("files-open", (w, _) =>
        {
            if (!shell.TryActivate(AppId.Files))
            {
                return Task.FromResult("error the Files app is not available");
            }

            if (w.Length > 0)
            {
                vm.Navigate(Place(w[0]));
            }

            return Task.FromResult("ok");
        });
        yield return new FilesCommand("files-nav", (w, _) =>
        {
            vm.Navigate(w.Length > 0 ? Place(w[0]) : null);
            return Task.FromResult("ok");
        });
        yield return new FilesCommand("files-select", (w, _) =>
        {
            if (w.Length == 1 && string.Equals(w[0], "none", StringComparison.Ordinal))
            {
                vm.ClearSelection();
            }
            else
            {
                vm.SelectMany(w);
            }

            return Task.FromResult("ok");
        });
        yield return new FilesCommand("files-do", async (w, ct) =>
        {
            if (w.Length == 0)
            {
                return "error usage: files-do COMMAND [KEYS]";
            }

            var registry = sp.GetRequiredService<Barbican.Presentation.Commands.ICommandRegistry>();
            if (!registry.TryGet(w[0], out var command))
            {
                return "error no such command " + w[0];
            }

            var arg = w.Length > 1 ? FilesMenus.Keys(w[1].Split(',')) : null;
            var ctx = new Barbican.Presentation.Commands.CommandContext(Barbican.Presentation.Commands.CommandSurface.Automation, [], null, arg, vm.AccountKey);
            if (!command.CanExecute(ctx))
            {
                return "error cannot run now";
            }

            var r = await command.ExecuteAsync(ctx, ct).ConfigureAwait(true);
            return r.IsOk ? "ok" : "error " + r.Error;
        });
        yield return new FilesCommand("files-drop-sim", async (w, ct) =>
        {
            var r = await actions.UploadPathsAsync(w, ct).ConfigureAwait(true);
            return r.IsOk ? "ok" : "error " + r.Error;
        });
        yield return new FilesCommand("files-pick", (w, _) =>
        {
            if (w.Length >= 2 && string.Equals(w[0], "folder", StringComparison.Ordinal))
            {
                picker.QueueFolder(w[1]);
            }
            else
            {
                picker.QueueFiles(w);
            }

            return Task.FromResult("ok");
        });
        yield return new FilesCommand("files-dump", (_, _) =>
        {
            var o = new JsonObject
            {
                ["location"] = vm.Location ?? "root",
                ["view"] = vm.ViewMode.ToString(),
                ["sort"] = vm.SortKey + (vm.SortDescending ? " desc" : " asc"),
                ["search"] = vm.SearchText,
                ["entries"] = new JsonArray([.. vm.Entries.Select(e => (JsonNode?)JsonValue.Create(e.Key + ":" + e.Name))]),
                ["selected"] = new JsonArray([.. vm.SelectedKeys.Select(k => (JsonNode?)JsonValue.Create(k))]),
                ["details"] = vm.ShowDetails,
                ["canUndo"] = actions.CanUndo,
                ["canPaste"] = actions.CanPaste,
            };
            return Task.FromResult(o.ToJsonString());
        });
    }
}
#endif
