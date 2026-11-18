// Mirrors: M5 plan section 6.4 (the Files views call FilesActions and the commands; the pointer handlers and the automation commands share this one entry, as CalendarUi does for the calendar). Every menu opens in-window through IContextMenuService (ADR 0007).

using Barbican.App.Shell;
using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Files;
using Barbican.Presentation.Menus;

namespace Barbican.App.Views.Files;

/// <summary>What the Files views share: the model, the actions, running commands, opening menus.</summary>
public static class FilesUi
{
    public static FilesViewModel Model => ViewHost.Get<FilesViewModel>();

    public static FilesActions Actions => ViewHost.Get<FilesActions>();

    /// <summary>Runs async work from an event handler: what it throws ends in an error toast, never in the dispatcher.</summary>
    public static void Run(Func<Task> work) => _ = GuardAsync(work);

    /// <summary>Runs a registered Files command with an argument (the keys); false when it cannot run now.</summary>
    public static bool RunCommand(string id, string? argument = null)
    {
        var registry = ViewHost.Find<ICommandRegistry>();
        if (registry is null || !registry.TryGet(id, out var command))
        {
            return false;
        }

        var context = new CommandContext(CommandSurface.Toolbar, [], null, argument, Model.AccountKey);
        if (!command.CanExecute(context))
        {
            return false;
        }

        (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<IToastService>())).Start(command, context);
        return true;
    }

    /// <summary>The menu of entries (empty keys: the background) at window coordinates.</summary>
    public static void ShowMenu(string menuId, double x, double y, IReadOnlyList<string> keys)
    {
        var menus = ViewHost.Find<IContextMenuService>();
        if (menus is null)
        {
            return;
        }

        var target = new FileMenuTarget(Model.AccountKey, keys, Model.Location is { } l && !l.StartsWith('*') ? l : null);
        menus.Show(new MenuContext(menuId, [], null, null, null, null, CommandSurface.ContextMenu) { File = target }, new MenuAnchor(x, y, null));
    }

    /// <summary>The menu id of an entry in the current place.</summary>
    public static string MenuFor(string place) => place switch
    {
        Barbican.Core.Files.FileTrash.TrashLocation => FilesMenuIds.Trash,
        FilesViewModel.FavouritesLocation => FilesMenuIds.Favourite,
        FilesViewModel.RecentLocation => FilesMenuIds.Recent,
        _ => FilesMenuIds.File,
    };

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / 1073741824.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GB",
    };

    public static string When(DateTimeOffset? t) => t is { } v ? v.LocalDateTime.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private static async Task GuardAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // files-data has not landed this seam: the view stays as it was
        }
#pragma warning disable CA1031 // an event handler must not throw: the failure becomes a toast
        catch (Exception e)
#pragma warning restore CA1031
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Files: " + e.Message, IsError: true));
        }
    }
}
