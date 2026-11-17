// M2 exit tests of stream search (plan 6.4): the Rules menu rows (MENU-READER section, MENU-MAIL More actions, the folder Rules submenu) and "Manage rules". I-179, I-065.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Rules;

namespace Barbican.Presentation.Tests.Rules;

public sealed class RuleMenusTests
{
    private static MailTarget Target(string id) => new("a", "acc", id, "t" + id, id, new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);

    private static MenuContext Context(params MailTarget[] targets) => new(MenuIds.Reader, targets, null, null, null, null, CommandSurface.ContextMenu);

    [Fact]
    public void The_section_names_every_rule_command_and_manage_rules()
    {
        var rows = RuleMenus.Section(Context(Target("1")), hasJunk: true);
        Assert.Equal(MenuItemKind.Separator, rows[0].Kind);
        Assert.Equal("Rules", rows[1].Label);
        var ids = rows.Select(r => r.CommandId).Where(c => c is not null).ToArray();
        Assert.Equal([CommandIds.RuleMarkRead, CommandIds.RuleBlockSender, CommandIds.ManageRules], ids);
        Assert.Equal([RuleMenus.MoveSender, RuleMenus.MoveDomain, RuleMenus.MoveList, RuleMenus.TagWith], rows.Where(r => r.Kind == MenuItemKind.Submenu).Select(r => r.SubmenuId).ToArray());
        Assert.All(rows.Where(r => r.Kind != MenuItemKind.Separator && r.Kind != MenuItemKind.Header && !string.Equals(r.CommandId, CommandIds.ManageRules, StringComparison.Ordinal)), r => Assert.True(r.Enabled, r.Label));
    }

    [Fact]
    public void Block_sender_says_there_is_no_junk_folder_and_rules_need_one_message()
    {
        var block = RuleMenus.Section(Context(Target("1")), hasJunk: false).Single(r => string.Equals(r.CommandId, CommandIds.RuleBlockSender, StringComparison.Ordinal));
        Assert.False(block.Enabled);
        Assert.Equal(QuickRuleCommands.NoJunkHint, block.Hint);

        var many = RuleMenus.Section(Context(Target("1"), Target("2")), hasJunk: true);
        Assert.All(many.Where(r => r.Kind == MenuItemKind.Submenu), r => Assert.False(r.Enabled));
    }

    [Fact]
    public void The_folder_menu_gets_a_rules_submenu_whose_manage_row_says_so()
    {
        var flyout = RuleMenus.FolderRulesFlyout();
        Assert.Equal(CommandIds.ManageRules, flyout[^1].CommandId);
        Assert.False(flyout[0].Enabled);
        Assert.Equal("Filters arrive in M6", QuickRuleService.ManageMessage);
    }
}
