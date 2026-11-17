// Mirrors: spec/menus.md MENU-READER and MENU-ATT (opened from the reader's More button, an attachment chip, or by automation); Bulwark components/email/email-viewer.tsx:5159.
// Builds the MenuContext of the message on screen (its target carries the account's mail account id, as RxSaveCommand does) and asks the menu service to show it. Owner reader.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;

namespace Barbican.App.Views.Reader;

/// <summary>Opens MENU-READER and MENU-ATT for the message on screen.</summary>
public sealed class ReaderMenuOpener(IContextMenuService menus, ReaderViewModel reader, IActiveMail active, IAccountManager? accounts = null)
{
    /// <summary>Shows MENU-READER (or MENU-ATT for <paramref name="attachment"/>) at <paramref name="anchor"/>; false when no message is open or the menu has no rows.</summary>
    public bool Open(string menuId, int? attachment, MenuAnchor anchor)
    {
        if (reader.Current.Body is not { } body || active.AccountKey is not { } key)
        {
            return false;
        }

        var accountId = accounts?.SessionFor(key)?.Client.Session.MailAccountId ?? key;
        var target = new MailTarget(key, accountId, body.Id, body.ThreadId ?? string.Empty, body.Id, body.MailboxIds, body.Keywords);
        menus.Show(new MenuContext(menuId, [target], null, null, null, attachment, CommandSurface.ContextMenu), anchor);
        return menus.IsOpen;
    }
}
