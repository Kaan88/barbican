#if AUTOMATION
// Mirrors: spec/automation-protocol.md (feature commands are IAutomationCommand registrations of the feature, M1-plan section 4.4); M4 CalendarCommand (the same shape); M5 plan section 3.1 (con-open, con-select, con-form-set, con-form-save, con-dump).
// D-M5-B04: they live here, not in src/Barbican.Automation/CommandRouter.Contacts.cs as the plan says, because the Automation project cannot see the App's types; the commands are still registered through the same IAutomationCommand seam and named con-*.
// Every command runs on the UI thread and answers one line. Nothing here injects pointer or key input: a click is what ContactsListView calls (ContactsViewModel and the registered commands).
//   con-open [all|duplicates|directory|book ID|group ID|category NAME]   opens the Contacts app and shows a place
//   con-select ID [extend] [open]                                         selects (and opens) a contact
//   con-search TEXT                                                       types into the search field
//   con-run COMMAND [ARGUMENT]                                            runs a registered contacts command by id (menus, keys and buttons do the same)
//   con-form-set KEY VALUE...                                             fills the open form: given, surname, org, email, phone, notes, categories
//   con-form-save                                                         saves the open form
//   con-queue-open PATH | con-queue-save PATH | con-queue-image PATH      answers the next file picker
//   con-dump [state|rows|sidebar|detail|menu]                             the state as one line of JSON

using System.Text.Json.Nodes;
using Barbican.Automation;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0051 // automation command tables read as one list of commands
#pragma warning disable MA0048 // the command record sits beside its table
namespace Barbican.App.Views.Contacts.Automation;

/// <summary>One contacts automation command.</summary>
public sealed class ContactsCommand(string name, Func<string[], CancellationToken, Task<string>> run) : IAutomationCommand
{
    /// <summary>How many commands <see cref="Create"/> returns.</summary>
    public const int Count = 11;

    public string Name => name;

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
        var vm = sp.GetRequiredService<ContactsViewModel>();
        var shell = sp.GetRequiredService<ShellViewModel>();
        var registry = sp.GetRequiredService<ICommandRegistry>();
        var picker = sp.GetRequiredService<ContactsFilePicker>();

        yield return new ContactsCommand("con-open", async (w, ct) =>
        {
            if (!shell.TryActivate(AppId.Contacts))
            {
                return "error the Contacts app is not available (the login has no contacts capability)";
            }

            var place = w.Length > 0 ? w[0].ToLowerInvariant() : "all";
            await vm.RefreshAsync(ct).ConfigureAwait(true);
            switch (place)
            {
                case "duplicates":
                    vm.ShowPlace(ContactsPlace.Duplicates, null);
                    break;
                case "directory":
                    vm.ShowPlace(ContactsPlace.Directory, null);
                    break;
                case "book" when w.Length > 1:
                    vm.ShowPlace(ContactsPlace.Book, w[1]);
                    break;
                case "group" when w.Length > 1:
                    vm.ShowPlace(ContactsPlace.Group, w[1]);
                    break;
                case "category" when w.Length > 1:
                    vm.ShowPlace(ContactsPlace.Category, string.Join(' ', w.Skip(1)));
                    break;
                default:
                    vm.ShowPlace(ContactsPlace.All, null);
                    break;
            }

            return "ok " + vm.Rows.Count + " rows";
        });

        yield return new ContactsCommand("con-select", (w, _) =>
        {
            if (w.Length == 0)
            {
                return Task.FromResult("error usage: con-select ID [extend] [open]");
            }

            vm.SelectContact(w[0], w.Contains("extend", StringComparer.Ordinal));
            if (w.Contains("open", StringComparer.Ordinal))
            {
                vm.Open(w[0]);
            }

            return Task.FromResult("ok selected=" + string.Join(',', vm.SelectedIds.Order(StringComparer.Ordinal)) + " open=" + (vm.OpenId ?? "-"));
        });

        yield return new ContactsCommand("con-search", (w, _) =>
        {
            vm.SearchText = string.Join(' ', w);
            return Task.FromResult("ok " + vm.Rows.Count + " rows");
        });

        yield return new ContactsCommand("con-run", async (w, ct) =>
        {
            if (w.Length == 0 || !registry.TryGet(w[0], out var command))
            {
                return "error no such command";
            }

            var context = new CommandContext(CommandSurface.Palette, [], null, w.Length > 1 ? string.Join(' ', w.Skip(1)).Replace('|', '\u001F') : null, vm.AccountKey);
            if (!command.CanExecute(context))
            {
                return "error the command cannot run now";
            }

            // a command that asks a question (a dialog) must not hold the automation line: it is started and the answer is the next command
            (sp.GetService<CommandRunner>() ?? new CommandRunner()).Start(command, context);
            await Task.Delay(250, ct).ConfigureAwait(true);
            return "ok started";
        });

        yield return new ContactsCommand("con-form-set", (w, _) =>
        {
            if (sp.GetRequiredService<IContactsEditors>() is not ContactsEditors { LastForm: { } form })
            {
                return Task.FromResult("error no form is open");
            }

            if (w.Length < 2)
            {
                return Task.FromResult("error usage: con-form-set KEY VALUE");
            }

            var value = string.Join(' ', w.Skip(1));
            switch (w[0].ToLowerInvariant())
            {
                case "given": form.Form = form.Form with { Given = value }; break;
                case "surname": form.Form = form.Form with { Surname = value }; break;
                case "org": form.Form = form.Form with { Org = value }; break;
                case "notes": form.Form = form.Form with { Notes = value }; break;
                case "categories": form.Categories = value; break;
                case "email":
                case "phone":
                    form.AddField(w[0].ToLowerInvariant());
                    form.UpdateField(form.Fields.Count - 1, f => f with { A = value });
                    break;
                default: return Task.FromResult("error unknown key " + w[0]);
            }

            return Task.FromResult("ok");
        });

        yield return new ContactsCommand("con-form-save", async (_, ct) =>
        {
            if (sp.GetRequiredService<IContactsEditors>() is not ContactsEditors { LastForm: { } form })
            {
                return "error no form is open";
            }

            var result = await form.SaveAsync(ct).ConfigureAwait(true);
            return result.IsOk ? "ok" : "error " + result.Error!.Message;
        });

        // the dialogs sit in a popup the element search cannot reach, so the hidden run drives their models the way their buttons do
        yield return new ContactsCommand("con-form-photo", async (_, ct) =>
        {
            if (sp.GetRequiredService<IContactsEditors>() is not ContactsEditors { LastForm: { } form })
            {
                return "error no form is open";
            }

            await form.PickPhotoAsync(ct).ConfigureAwait(true);
            return string.IsNullOrEmpty(form.Error) ? "ok photo=" + (form.PhotoUri is null ? "none" : "set") : "error " + form.Error;
        });

        yield return new ContactsCommand("con-import-go", async (_, ct) =>
        {
            if (sp.GetRequiredService<ContactsActions>().LastImport is not { } import)
            {
                return "error no import dialog is open";
            }

            await import.PickAsync(ct).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(import.Error))
            {
                return "error " + import.Error;
            }

            var count = import.ImportCount;
            var result = await import.ImportAsync(ct).ConfigureAwait(true);
            return result.IsOk ? "ok imported=" + result.Value + " of " + count : "error " + result.Error!.Message + " imported=" + result.Value;
        });

        yield return new ContactsCommand("con-merge-go", async (_, ct) =>
        {
            if (sp.GetRequiredService<ContactsActions>().LastMerge is not { } merge)
            {
                return "error no merge dialog is open";
            }

            var result = await merge.MergeAsync(ct).ConfigureAwait(true);
            return result.IsOk ? "ok " + merge.Status : "error " + result.Error!.Message;
        });

        yield return new ContactsCommand("con-queue-open", async (w, ct) =>
        {
            var path = string.Join(' ', w);
            if (w.Length == 0 || !File.Exists(path))
            {
                return "error no such file";
            }

            if (path.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase))
            {
                picker.QueueOpen(new ContactsPickedFile(Path.GetFileName(path), await File.ReadAllTextAsync(path, ct).ConfigureAwait(true)));
            }
            else
            {
                picker.QueueImage(new ContactsPickedImage(Path.GetFileName(path), await File.ReadAllBytesAsync(path, ct).ConfigureAwait(true)));
            }

            return "ok";
        });

        yield return new ContactsCommand("con-dump", (w, _) =>
        {
            var what = w.Length > 0 ? w[0].ToLowerInvariant() : "state";
            var json = new JsonObject();
            switch (what)
            {
                case "rows":
                    json["rows"] = new JsonArray([.. vm.Rows.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["name"] = r.DisplayName, ["letter"] = r.Letter, ["initials"] = r.Initials, ["selected"] = vm.SelectedIds.Contains(r.Id) })]);
                    break;
                case "sidebar":
                    json["books"] = new JsonArray([.. vm.Books.Select(b => (JsonNode)new JsonObject { ["id"] = b.Id, ["name"] = b.Name, ["count"] = b.Count, ["default"] = b.IsDefault })]);
                    json["groups"] = new JsonArray([.. vm.Groups.Select(g => (JsonNode)new JsonObject { ["id"] = g.Id, ["name"] = g.Name, ["count"] = g.Count })]);
                    json["categories"] = new JsonArray([.. vm.Categories.Select(c => (JsonNode)new JsonObject { ["name"] = c.Name, ["count"] = c.Count })]);
                    json["duplicateSets"] = vm.DuplicateSetCount;
                    json["directory"] = vm.DirectoryAvailable;
                    break;
                case "detail":
                    json["open"] = vm.OpenId;
                    json["name"] = vm.OpenCard is { } card ? ContactsListRules.FullName(card) : null;
                    json["lines"] = new JsonArray([.. vm.DetailLines.Select(l => (JsonNode)new JsonObject { ["section"] = l.Section, ["label"] = l.Label, ["value"] = l.Value })]);
                    break;
                case "photo":
                    json["open"] = vm.OpenId;
                    json["uriLength"] = vm.OpenCard is { } pc ? ContactsJson.PhotoUri(pc)?.Length : null;
                    json["media"] = vm.OpenCard?["media"] is { } media ? media.ToJsonString().Length : null;
                    json["decoded"] = vm.OpenCard is { } dc && ContactsAvatar.Photo(ContactsJson.PhotoUri(dc)) is not null;
                    break;
                case "menu":
                    var provider = sp.GetServices<IMenuProvider>().FirstOrDefault(p => string.Equals(p.MenuId, w.Length > 1 ? w[1] : ContactsMenuIds.Contact, StringComparison.Ordinal));
                    var ids = vm.SelectedIds.Count > 0 ? vm.SelectedIds.ToList() : vm.OpenId is { } open ? [open] : [];
                    var target = new ContactMenuTarget(vm.AccountKey ?? string.Empty, ids, GroupId: vm.Place == ContactsPlace.Group ? vm.PlaceId : null, Category: vm.Place == ContactsPlace.Category ? vm.PlaceId : null, BookId: vm.Place == ContactsPlace.Book ? vm.PlaceId : null);
                    var items = provider?.Build(new MenuContext(provider.MenuId, [], null, null, null, null, CommandSurface.ContextMenu) { Contact = target }) ?? [];
                    json["items"] = new JsonArray([.. items.Select(i => (JsonNode)JsonValue.Create(i.Kind == MenuItemKind.Separator ? "-" : i.Label + (i.Enabled ? string.Empty : " (off)"))!)]);
                    break;
                default:
                    json["place"] = vm.Place.ToString();
                    json["placeId"] = vm.PlaceId;
                    json["rows"] = vm.Rows.Count;
                    json["selected"] = new JsonArray([.. vm.SelectedIds.Order(StringComparer.Ordinal).Select(id => (JsonNode)JsonValue.Create(id)!)]);
                    json["open"] = vm.OpenId;
                    json["search"] = vm.SearchText;
                    json["loading"] = vm.IsLoading;
                    json["error"] = vm.LoadError;
                    json["sortByLast"] = vm.SortByLastName;
                    json["groupByLetter"] = vm.GroupByLetter;
                    json["selectMode"] = vm.SelectMode;
                    json["duplicateSets"] = vm.DuplicateSetCount;
                    json["lastSaved"] = picker.LastSavedName;
                    break;
            }

            return Task.FromResult(json.ToJsonString());
        });

    }
}
#endif
