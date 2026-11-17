// Mirrors: spec/menus.md MENU-READER "Rules" section (Always move messages from <sender> >, from @domain >, from this list >, Always mark as read, Always tag with >, Block sender,
// Manage rules) and its flyouts; Bulwark components/email/rules-menu.tsx (RulesMenuItems :208, FolderRulesSubMenu :1005, FolderRulesContent :928). I-179, I-065 (Rules in the folder menu).
// The rows are data; the commands they name are QuickRuleCommands. The sender and domain are not known when the menu opens (the message is read when a rule is made), so the labels say
// "the sender" and "this domain", the wording the spec allows.

using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Rules;

/// <summary>The Rules menu rows and their flyouts.</summary>
public static class RuleMenus
{
    /// <summary>The flyout of "Always move messages from the sender".</summary>
    public const string MoveSender = "MENU-RULES>move-sender";

    /// <summary>The flyout of "Always move messages from this domain".</summary>
    public const string MoveDomain = "MENU-RULES>move-domain";

    /// <summary>The flyout of "Always move messages from this list".</summary>
    public const string MoveList = "MENU-RULES>move-list";

    /// <summary>The flyout of "Always tag with".</summary>
    public const string TagWith = "MENU-RULES>tag";

    /// <summary>The flyout of the folder menu's Rules row.</summary>
    public const string FolderRules = "MENU-FOLDER>rules";

    private const string Icon = "Opt";

    /// <summary>How many providers <see cref="Providers"/> returns (the registration adds one per index).</summary>
    public const int Count = 7;

    /// <summary>The Rules section for a menu about messages: a separator, the heading and the rows (append it to MENU-READER and MENU-MAIL's More actions).</summary>
    public static IReadOnlyList<MenuItemSpec> Section(MenuContext c, bool hasJunk)
    {
        ArgumentNullException.ThrowIfNull(c);
        var one = c.Targets.Count == 1;
        var block = Item("Alert", "Block sender", CommandIds.RuleBlockSender, enabled: one && hasJunk);
        return
        [
            Sep(),
            Header("Rules"),
            Sub(Icon, "Always move messages from the sender", MoveSender, one),
            Sub(Icon, "Always move messages from this domain", MoveDomain, one),
            Sub(Icon, "Always move messages from this list", MoveList, one),
            Item(MenuGlyphs.Read, "Always mark as read from the sender", CommandIds.RuleMarkRead, enabled: one),
            Sub(MenuGlyphs.Tag, "Always tag with", TagWith, one),
            hasJunk ? block : block with { Hint = QuickRuleCommands.NoJunkHint },
            Item("Settings", "Manage rules", CommandIds.ManageRules),
        ];
    }

    /// <summary>A flyout listing the folders that take mail; each row runs <paramref name="commandId"/> with the folder id.</summary>
    public static IReadOnlyList<MenuItemSpec> FolderFlyout(MenuContext c, IMenuDataSource d, string commandId)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var account = c.Targets.Count > 0 ? c.Targets[0].AccountKey : c.Mailbox?.AccountKey;
        var items = d.Folders(account)
            .Where(f => !f.IsVirtual && f.Role != Core.Mail.Model.MailboxRole.Drafts && f.Rights.MayAddItems)
            .Select(f => Item(MenuGlyphs.Folder, f.Name, commandId, argument: f.Id, indent: f.Depth))
            .ToList();
        return items.Count == 0 ? [Note("No folders")] : items;
    }

    /// <summary>The "Always tag with" flyout: every tag with its colour.</summary>
    public static IReadOnlyList<MenuItemSpec> TagFlyout(IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var items = d.Tags().Select(t => Item(MenuGlyphs.Dot(t.ColorRgb), t.Label, CommandIds.RuleTag, argument: t.Id)).ToList();
        return items.Count == 0 ? [Note("No tags")] : items;
    }

    /// <summary>The folder menu's Rules flyout: running a rule on a folder is M6, managing rules says so.</summary>
    public static IReadOnlyList<MenuItemSpec> FolderRulesFlyout() =>
    [
        Note("Running rules on a folder arrives in M6"),
        Sep(),
        Item("Settings", "Manage rules", CommandIds.ManageRules),
    ];

    /// <summary>The providers of this stream: the decorated MENU-MAIL "More actions" and MENU-FOLDER (the later registration of an id wins), and the flyouts.</summary>
    public static IReadOnlyList<IMenuProvider> Providers(IMenuDataSource d, Func<string?, bool> hasJunk)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(hasJunk);
        return
        [
            new DelegateMenuProvider(MenuIds.MailMore, c => [.. MailMenus.More(c), .. Section(c, hasJunk(c.Targets.Count > 0 ? c.Targets[0].AccountKey : null))]),
            new DelegateMenuProvider(MenuIds.Folder, c => WithRules(FolderMenus.Folder(c, d))),
            new DelegateMenuProvider(MoveSender, c => FolderFlyout(c, d, CommandIds.RuleMoveSender)),
            new DelegateMenuProvider(MoveDomain, c => FolderFlyout(c, d, CommandIds.RuleMoveDomain)),
            new DelegateMenuProvider(MoveList, c => FolderFlyout(c, d, CommandIds.RuleMoveList)),
            new DelegateMenuProvider(TagWith, _ => TagFlyout(d)),
            new DelegateMenuProvider(FolderRules, _ => FolderRulesFlyout()),
        ];
    }

    private static IReadOnlyList<MenuItemSpec> WithRules(IReadOnlyList<MenuItemSpec> folder)
    {
        if (folder.Count == 0)
        {
            return folder;
        }

        // after "Import messages", before the danger rows; a folder menu without that row gets it at the end
        var at = folder.ToList().FindLastIndex(i => string.Equals(i.CommandId, CommandIds.ImportMessages, StringComparison.Ordinal) || string.Equals(i.CommandId, CommandIds.ExportEml, StringComparison.Ordinal));
        var rules = Sub(Icon, "Rules", FolderRules);
        return at < 0 ? [.. folder, rules] : [.. folder.Take(at + 1), rules, .. folder.Skip(at + 1)];
    }
}
