#pragma warning disable MA0002 // ordinal by nature
// Builders shared by the menu tests: targets, contexts and a registry that holds a fake for every command id. Owner menus.

using System.Reflection;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Tests.Menus;

internal static class MenuKit
{
    public static MailTarget Target(string id, params string[] keywords) =>
        new("acct", "a1", id, "t" + id, id, new Dictionary<string, bool> { ["inbox"] = true }, Keywords.Of(keywords));

    public static MenuContext Context(string menuId, string? role = "inbox", string mailboxId = "inbox", string? tag = null, string? address = null, params MailTarget[] targets) =>
        new(menuId, targets, new MailboxRef("acct", "a1", mailboxId, role), tag, address, null, CommandSurface.ContextMenu);

    /// <summary>Every command id of <see cref="CommandIds"/> with a fake handler.</summary>
    public static Dictionary<string, FakeCommand> AllCommands() =>
        typeof(CommandIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToDictionary(id => id, id => new FakeCommand(id), StringComparer.Ordinal);

    public static MenuModelBuilder Builder(FakeMenuData data, IEnumerable<FakeCommand> commands) =>
        new(MenuProviders.Create(data), new CommandRegistry(commands));

    public static string[] Labels(IEnumerable<MenuItemSpec> items) =>
        items.Select(i => i.Kind switch { MenuItemKind.Separator => "—", MenuItemKind.Header => "# " + i.Label, MenuItemKind.Submenu => i.Label + " ›", _ => i.Label }).ToArray();
}
