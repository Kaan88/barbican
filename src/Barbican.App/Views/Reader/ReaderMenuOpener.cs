// Mirrors: spec/menus.md MENU-READER and MENU-ATT (opened from the reader's More button, an attachment chip, or by automation); Bulwark components/email/email-viewer.tsx:5159.
// Builds the MenuContext of the message on screen (its target follows the MailTarget contract, as RxSaveCommand's) and asks the menu service to show it. Owner reader.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;

namespace Barbican.App.Views.Reader;

/// <summary>Opens MENU-READER and MENU-ATT for the message on screen.</summary>
public sealed class ReaderMenuOpener(IContextMenuService menus, ReaderViewModel reader, IActiveMail active, ICommandRegistry commands, CommandRunner runner)
{
    /// <summary>Shows MENU-READER (or MENU-ATT for <paramref name="attachment"/>) at <paramref name="anchor"/>; false when no message is open or the menu has no rows.</summary>
    public bool Open(string menuId, int? attachment, MenuAnchor anchor)
    {
        if (reader.Current.Body is not { } body || active.AccountKey is not { } key)
        {
            return false;
        }

        // the MailTarget contract (Fable review 3 R3-02): the shared account or empty, the list id; the saver resolves login and server id
        var target = MailTarget.OfOpen(key, body);
        menus.Show(new MenuContext(menuId, [target], null, null, null, attachment, CommandSurface.ContextMenu), anchor);
        return menus.IsOpen;
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

}
