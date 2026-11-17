// Mirrors: spec/menus.md MENU-TAG; Rust ctxmenu.rs request (the "tag" kind) :150, submenu "color" :575, confirmation :848; Bulwark components/layout/sidebar.tsx
// (the tag row menu), lib/tag-utils.ts (nested ids). Owner menus.

using Barbican.Core.Mail.Tags;
using Barbican.Presentation.Commands;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Menus;

/// <summary>MENU-TAG and its Colour flyout.</summary>
public static class TagMenus
{
    /// <summary>The confirmation's danger label for deleting a tag (ctxmenu.rs:848).</summary>
    public const string DeleteConfirm = "Delete this tag (messages keep it)";

    /// <summary>The main menu of a tag; <see cref="MenuContext.TagId"/> names it.</summary>
    public static IReadOnlyList<MenuItemSpec> Tag(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var id = c.TagId;
        if (string.IsNullOrEmpty(id))
        {
            return [];
        }

        var items = new List<MenuItemSpec>
        {
            Item(MenuGlyphs.Search, "Show messages", CommandIds.ShowTagMessages, argument: id),
            Item(MenuGlyphs.Read, "Mark all as read", CommandIds.MarkTagRead, argument: id),
            Sep(),
            Item(MenuGlyphs.Add, "New tag…", CommandIds.NewTag),
        };
        if (d.NestedTags && TagRules.ValidId(id + TagRules.Separator + "a"))
        {
            items.Add(Item(MenuGlyphs.Add, "New tag inside…", CommandIds.NewTagInside, argument: id));
        }

        items.Add(Item(MenuGlyphs.Rename, "Rename…", CommandIds.RenameTag, argument: id));
        items.Add(Sub(MenuGlyphs.Tag, "Colour", MenuIds.TagColour));
        items.Add(Danger(MenuGlyphs.Delete, "Delete tag", CommandIds.DeleteTag, confirm: DeleteConfirm, argument: id));
        items.Add(Sep());
        items.Add(new MenuItemSpec(MenuItemKind.Item, "Manage tags…", CommandIds.ManageTags, Hint: "Settings arrive in M6", Icon: MenuGlyphs.Tag, Enabled: false));
        return items;
    }

    /// <summary>The tag palette, each in its colour, the current one checked.</summary>
    public static IReadOnlyList<MenuItemSpec> Colour(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var current = d.Tags().FirstOrDefault(t => string.Equals(t.Id, c.TagId, StringComparison.Ordinal))?.Color;
        return TagRules.Palette
            .Select(name => Item(
                MenuGlyphs.Dot(TagRules.ColorRgb(name)),
                char.ToUpperInvariant(name[0]) + name[1..],
                CommandIds.SetTagColour,
                argument: c.TagId + "|" + name,
                check: string.Equals(current, name, StringComparison.Ordinal)))
            .ToList();
    }
}
