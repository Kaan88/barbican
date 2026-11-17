#if AUTOMATION
// Mirrors: spec/automation-protocol.md `menu-open MENU TARGET`, `menu-items`, `menu-pick LABEL`, `rclick KIND TARGET`, `palette TEXT`, `palette-pick N`, `dialog-state`, `dialog-open`,
// `dialog-answer`: no input is produced; the commands drive the controllers the views draw (ADR 0007). Owner menus.

using Barbican.Automation;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Palette;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Views.Menus.Automation;

/// <summary>Registers the menus stream's automation commands.</summary>
public static class MenusAutomationRegistration
{
    // MA0051: a command table (one case per protocol command) reads top to bottom; splitting it scatters the protocol (as .editorconfig says for src/Barbican.Automation).
#pragma warning disable MA0051
    public static IServiceCollection AddBarbicanMenusAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Add(services, "menu-open", (sp, args) =>
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Task.FromResult(parts.Length == 0 ? "error usage: menu-open MENU [TARGET]" : sp.GetRequiredService<ContextPointerRouter>().OpenFor(parts[0], parts.Length > 1 ? parts[1] : string.Empty));
        });
        Add(services, "rclick", (sp, args) =>
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var menu = parts.Length == 0 ? null : parts[0].ToLowerInvariant() switch
            {
                "row" => MenuIds.Mail,
                "folder" => MenuIds.Folder,
                "tag" => MenuIds.Tag,
                "list" => MenuIds.List,
                "thread" => MenuIds.Thread,
                _ => null,
            };
            return Task.FromResult(menu is null ? "error usage: rclick row|folder|tag|list [TARGET]" : sp.GetRequiredService<ContextPointerRouter>().OpenFor(menu, parts.Length > 1 ? parts[1] : string.Empty));
        });
        Add(services, "menu-items", (sp, _) => Task.FromResult(Describe(sp.GetRequiredService<ContextMenuController>())));
        Add(services, "menu-pick", async (sp, args) => await sp.GetRequiredService<ContextMenuController>().PickAsync(args.Trim()) ? "ok" : "error no such row");
        Add(services, "palette", async (sp, args) =>
        {
            var palette = sp.GetRequiredService<PaletteController>();
            await palette.OpenAsync();
            if (args.Length > 0)
            {
                await palette.SetTextAsync(args);
            }

            var rows = palette.Rows.Where(r => !r.IsHeader).Select(r => (r.Enabled ? string.Empty : "!") + r.Command!.Label).ToList();
            return $"{rows.Count} rows: " + string.Join(" | ", rows.Take(8));
        });
        Add(services, "palette-pick", async (sp, args) =>
        {
            var palette = sp.GetRequiredService<PaletteController>();
            return int.TryParse(args.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && await palette.PickAsync(n) ? "ok" : "error cannot run";
        });
        Add(services, "dialog-state", (sp, _) =>
        {
            var dialogs = sp.GetRequiredService<DialogService>();
            return Task.FromResult(dialogs.Current is { } d ? $"open {d.Request.GetType().Name} title={d.Title} text={d.Text}" : "none");
        });
        Add(services, "dialog-open", (sp, args) =>
        {
            var dialogs = sp.GetRequiredService<DialogService>();
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return Task.FromResult("error usage: dialog-open prompt|confirm [TITLE]");
            }

            var title = parts.Length > 1 ? parts[1] : "Dialog";
            switch (parts[0])
            {
                case "prompt":
                    _ = dialogs.ShowAsync(new PromptDialogRequest(title, "Name"), CancellationToken.None);
                    break;
                case "confirm":
                    _ = dialogs.ShowAsync(new ConfirmDialogRequest(title, "Are you sure?", "Delete", Danger: true), CancellationToken.None);
                    break;
                default:
                    break;
            }

            return Task.FromResult(dialogs.IsOpen ? "ok" : "error unknown dialog");
        });
        Add(services, "dialog-answer", (sp, args) =>
        {
            var dialogs = sp.GetRequiredService<DialogService>();
            if (dialogs.Current is not { } d)
            {
                return Task.FromResult("error no dialog");
            }

            d.Complete(args.Trim().ToLowerInvariant() switch { "ok" => d.Request is PromptDialogRequest ? d.Text.Trim() : true, _ => null });
            return Task.FromResult("ok");
        });
        return services;
    }
#pragma warning restore MA0051

    private static void Add(IServiceCollection services, string name, Func<IServiceProvider, string, Task<string>> run) =>
        services.AddSingleton<IAutomationCommand>(sp => new LambdaAutomationCommand(name, args => run(sp, args)));

    private static string Describe(ContextMenuController menu)
    {
        if (!menu.IsOpen || menu.Root is null)
        {
            return "closed";
        }

        static string Rows(MenuLevel level) => string.Join(" | ", level.Items.Select(i => i.Kind switch
        {
            MenuItemKind.Separator => "-",
            MenuItemKind.Header => "# " + i.Label,
            _ => (i.Enabled ? string.Empty : "!") + i.Label.Trim() + (i.Kind == MenuItemKind.Submenu ? " >" : string.Empty) + (i.Checked == true ? " [x]" : string.Empty),
        }));
        return (menu.Confirming ? "confirm: " : "menu: ") + Rows(menu.Root) + (menu.Flyout is { } fly ? " || flyout: " + Rows(fly) : string.Empty);
    }
}
#endif
