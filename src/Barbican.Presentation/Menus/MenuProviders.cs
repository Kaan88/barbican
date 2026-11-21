// Mirrors: M2 plan section 4.2 (menus provides MENU-MAIL, -THREAD, -ADDR, -BODY, -FOLDER, -TAG, -LIST, ACCOUNT, FOLDERS-HEADER). Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>The providers of this stream, over one data source.</summary>
public static class MenuProviders
{
    /// <summary>How many providers <see cref="Create"/> returns (the registration adds one <see cref="IMenuProvider"/> per index).</summary>
    public const int Count = 15;

    public static IReadOnlyList<IMenuProvider> Create(IMenuDataSource data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return
        [
            new DelegateMenuProvider(MenuIds.Mail, c => MailMenus.Main(c, data)),
            new DelegateMenuProvider(MenuIds.MailMove, c => MailMenus.Move(c, data)),
            new DelegateMenuProvider(MenuIds.MailTag, c => MailMenus.Tag(c, data)),
            new DelegateMenuProvider(MenuIds.MailMore, MailMenus.More),
            new DelegateMenuProvider(MenuIds.Thread, c => MessageMenus.Thread(c, data)),
            new DelegateMenuProvider(MenuIds.Address, MessageMenus.Address),
            new DelegateMenuProvider(MenuIds.Body, _ => MessageMenus.Body(data)),
            new DelegateMenuProvider(MenuIds.Folder, c => FolderMenus.Folder(c, data)),
            new DelegateMenuProvider(MenuIds.FoldersHeader, _ => FolderMenus.FoldersHeader()),
            new DelegateMenuProvider(MenuIds.List, c => FolderMenus.List(c, data)),
            new DelegateMenuProvider(MenuIds.ListActions, c => FolderMenus.ListActions(c, data)),
            new DelegateMenuProvider(MenuIds.ListSort, c => FolderMenus.ListSort(c, data)),
            new DelegateMenuProvider(MenuIds.Tag, c => TagMenus.Tag(c, data)),
            new DelegateMenuProvider(MenuIds.TagColour, c => TagMenus.Colour(c, data)),
            new DelegateMenuProvider(MenuIds.Account, AccountMenus.Account),
        ];
    }
}
