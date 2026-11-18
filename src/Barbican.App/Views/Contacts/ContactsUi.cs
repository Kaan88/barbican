// Mirrors: M4 CalendarUi (what the views do with a click, a double click or a right click: one place, so the pointer handlers, the keys and the automation commands take the same road); M5 plan section 6.2 (the menus open through IContextMenuService and the views never build an item).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Menus;

namespace Barbican.App.Views.Contacts;

/// <summary>Helpers of the Contacts views.</summary>
internal static class ContactsUi
{
    public static ContactsViewModel Model => ViewHost.Get<ContactsViewModel>();

    public static ContactsActions Actions => ViewHost.Get<ContactsActions>();

    public static SolidColorBrush Palette(string key) => AppResources.Brush(key);

    /// <summary>Runs async work from an event handler: what it throws ends in an error toast, never in the dispatcher.</summary>
    public static void Run(Func<Task> work) => _ = GuardAsync(work);

    private static async Task GuardAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // a seam of another stream (the data layer) that has not landed: the view stays as it was
        }
#pragma warning disable CA1031 // an event handler must not throw: the failure becomes a toast
        catch (Exception e)
#pragma warning restore CA1031
        {
            _ = ViewHost.Find<IToastService>()?.Show(new ToastRequest("Contacts: " + e.Message, IsError: true));
        }
    }

    /// <summary>Runs a registered command by id with an argument; false when nobody registered it or it cannot run now.</summary>
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

    /// <summary>Whether a command is registered and enabled now (a button greys what cannot run).</summary>
    public static bool CanRun(string id, string? argument = null) =>
        ViewHost.Find<ICommandRegistry>() is { } registry && registry.TryGet(id, out var command) && command.CanExecute(new CommandContext(CommandSurface.Toolbar, [], null, argument, Model.AccountKey));

    /// <summary>A contacts menu at window coordinates.</summary>
    public static void ShowMenu(string menuId, double x, double y, ContactMenuTarget target)
    {
        var menus = ViewHost.Find<IContextMenuService>();
        if (menus is null)
        {
            return;
        }

        menus.Show(new MenuContext(menuId, [], null, null, null, null, CommandSurface.ContextMenu) { Contact = target }, new MenuAnchor(x, y, null));
    }

    /// <summary>The menu of a contact: when it is not selected the right click selects it alone first; with several selected the menu is the multi-select one.</summary>
    public static void ShowContactMenu(string id, double x, double y)
    {
        var vm = Model;
        if (!vm.SelectedIds.Contains(id))
        {
            vm.SelectContact(id, false);
        }

        var key = vm.AccountKey;
        if (key is null)
        {
            return;
        }

        var ids = vm.SelectedIds.Count > 1 ? vm.SelectedIds.ToList() : [id];
        ShowMenu(ContactsMenuIds.Contact, x, y, new ContactMenuTarget(key, ids));
    }
}
