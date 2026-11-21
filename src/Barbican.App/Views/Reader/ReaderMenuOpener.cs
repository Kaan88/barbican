// Mirrors: spec/menus.md MENU-READER and MENU-ATT (opened from the reader's More button, an attachment chip, or by automation); Bulwark components/email/email-viewer.tsx:5159.
// Builds the MenuContext of the message on screen (its target follows the MailTarget contract, as RxSaveCommand's) and asks the menu service to show it. Owner reader.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;

namespace Barbican.App.Views.Reader;

/// <summary>Opens MENU-READER and MENU-ATT for the message on screen.</summary>
public sealed class ReaderMenuOpener(IContextMenuService menus, ReaderViewModel reader, IActiveMail active, ICommandRegistry commands, CommandRunner runner, MessageListViewModel list, IMenuDataSource data)
{
    /// <summary>Shows MENU-READER (or MENU-ATT for <paramref name="attachment"/>) at <paramref name="anchor"/>; false when no message is open or the menu has no rows.</summary>
    public bool Open(string menuId, int? attachment, MenuAnchor anchor, bool includeOverflow = false)
    {
        if (Context(menuId, attachment) is not { } context) return false;
        menus.Show(context with { IncludeReaderOverflow = includeOverflow }, anchor);
        return menus.IsOpen;
    }

    internal IEnumerable<MenuItemSpec> ToolbarActions() => Context(MenuIds.Reader, null) is { } context
        ? Barbican.Presentation.Reader.ReaderMenus.ToolbarActions(context, data) : [];

    private MenuContext? Context(string menuId, int? attachment)
    {
        if (reader.Current.Body is not { } body || active.AccountKey is not { } key ||
            !string.Equals(reader.Current.ListAccountKey, key, StringComparison.Ordinal)) return null;
        var target = MailTarget.OfOpen(key, body);
        var folder = list.MailboxId is { } id ? new MailboxRef(key, target.AccountId, id, list.Role.ToString().ToLowerInvariant()) : null;
        return new MenuContext(menuId, [target], folder, null, null, attachment, CommandSurface.ContextMenu);
    }

    /// <summary>Activates an attachment using the current reader target and preference-aware command.</summary>
    public bool ActivateAttachment(int index)
    {
        if (reader.Current.Body is not { } body || active.AccountKey is not { } key || !commands.TryGet(CommandIds.OpenAttachment, out var command)) return false;
        var context = Barbican.Presentation.Reader.ReaderCommands.AttachmentContext(key, body, index);
        if (!command.CanExecute(context)) return false;
        runner.Start(command, context, toastFailure: true);
        return true;
    }

    /// <summary>Saves an attachment (the chip's download button, Bulwark reader-attachments.tsx:200).</summary>
    public bool SaveAttachment(int index)
    {
        if (reader.Current.Body is not { } body || active.AccountKey is not { } key || !commands.TryGet(CommandIds.SaveAttachment, out var command)) return false;
        var context = Barbican.Presentation.Reader.ReaderCommands.AttachmentContext(key, body, index);
        if (!command.CanExecute(context)) return false;
        runner.Start(command, context, toastFailure: true);
        return true;
    }
}
