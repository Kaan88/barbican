// Mirrors: Bulwark components/layout/account-switcher.tsx (the per-account actions: set as default, sign out, sign out of all, manage accounts),
// spec I-013, I-014, I-015, I-031; Rust accounts menu. The logic is unified's IAccountMaintenance; this is the menu. Owner menus.

using Barbican.Presentation.Commands;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Menus;

/// <summary>The menu of one account (the "ACCOUNT" menu): <see cref="MenuContext.Address"/> holds its account key.</summary>
public static class AccountMenus
{
    /// <summary>The confirmation label for signing one account out.</summary>
    public const string SignOutConfirm = "Sign out of this account";

    /// <summary>The confirmation label for signing every account out.</summary>
    public const string SignOutAllConfirm = "Sign out of every account";

    public static IReadOnlyList<MenuItemSpec> Account(MenuContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var key = c.Address;
        if (string.IsNullOrEmpty(key))
        {
            return [];
        }

        return
        [
            Item(MenuGlyphs.Default, "Set as default", CommandIds.SetDefaultAccount, argument: key),
            Sep(),
            Danger(MenuGlyphs.SignOut, "Sign out", CommandIds.SignOut, confirm: SignOutConfirm, argument: key),
            Danger(MenuGlyphs.SignOut, "Sign out of all accounts", CommandIds.SignOutAll, confirm: SignOutAllConfirm),
            Sep(),
            new MenuItemSpec(MenuItemKind.Item, "Manage accounts", CommandIds.ManageAccounts, Hint: "Settings arrive in M6", Icon: MenuGlyphs.Contact, Enabled: false),
        ];
    }
}
