// Mirrors: M2 plan section 4.1 ("a command id with no handler shows disabled", enable state evaluated when a menu opens) and 4.2 (IMenuProvider);
// spec/menus.md "Open questions" (no stale enable state); Bulwark lib/command-registry.ts:53-96. Owner menus.

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Menus;

/// <summary>Builds the rows of a menu: the provider's rows, with every row that runs a command enabled only when the command exists and can run now.</summary>
public sealed class MenuModelBuilder(IEnumerable<IMenuProvider> providers, ICommandRegistry registry)
{
    private readonly Dictionary<string, IMenuProvider> _byId = Index(providers);

    /// <summary>True when some provider builds this menu id.</summary>
    public bool Has(string menuId) => _byId.ContainsKey(menuId);

    /// <summary>The rows now (the providers and every command's <c>CanExecute</c> are asked each time, nothing is cached); empty when no provider or no rows.</summary>
    public IReadOnlyList<MenuItemSpec> Build(MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_byId.TryGetValue(context.MenuId, out var provider))
        {
            return [];
        }

        var rows = provider.Build(context);
        var account = context.Targets.Count > 0 ? context.Targets[0].AccountKey : context.Mailbox?.AccountKey;
        var result = new List<MenuItemSpec>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(Gate(row, context, account));
        }

        return result;
    }

    /// <summary>The command context a row runs with.</summary>
    public static CommandContext CommandContextOf(MenuContext context, MenuItemSpec item)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        var account = context.Targets.Count > 0 ? context.Targets[0].AccountKey : context.Mailbox?.AccountKey;
        return new CommandContext(context.Surface, context.Targets, context.Mailbox, item.Argument, account);
    }

    private MenuItemSpec Gate(MenuItemSpec row, MenuContext context, string? account)
    {
        if (row.Kind != MenuItemKind.Item || row.CommandId is null || !row.Enabled)
        {
            return row;
        }

        var ok = registry.TryGet(row.CommandId, out var command)
            && command.CanExecute(new CommandContext(context.Surface, context.Targets, context.Mailbox, row.Argument, account));
        return ok ? row : row with { Enabled = false };
    }

    private static Dictionary<string, IMenuProvider> Index(IEnumerable<IMenuProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var map = new Dictionary<string, IMenuProvider>(StringComparer.Ordinal);
        foreach (var p in providers)
        {
            map[p.MenuId] = p;
        }

        return map;
    }
}
