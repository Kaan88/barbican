// Mirrors: Rust ctxmenu.rs row_h :94 (30 px rows, 9 px separators, 24 px headers) and hover :134 (the highlighted row). Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>One list of rows in an open menu (the menu itself, or a flyout) and its highlighted row.</summary>
public sealed class MenuLevel(IReadOnlyList<MenuItemSpec> items, int parentIndex)
{
    public const double RowHeight = 30;
    public const double SeparatorHeight = 9;
    public const double HeaderHeight = 24;

    public IReadOnlyList<MenuItemSpec> Items { get; } = items;

    /// <summary>The highlighted row, -1 for none.</summary>
    public int Highlight { get; set; } = -1;

    /// <summary>The row of the parent level this flyout hangs from, -1 for the menu itself.</summary>
    public int ParentIndex { get; } = parentIndex;

    /// <summary>The pixel height of one row.</summary>
    public static double HeightOf(MenuItemSpec item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            MenuItemKind.Separator => SeparatorHeight,
            MenuItemKind.Header => HeaderHeight,
            _ => RowHeight,
        };
    }

    /// <summary>The pixel height of the whole list.</summary>
    public double Height => Items.Sum(HeightOf);

    /// <summary>The top of a row inside the list.</summary>
    public double TopOf(int index) => Items.Take(index).Sum(HeightOf);

    /// <summary>A row the keyboard and pointer can land on: an enabled item or flyout.</summary>
    public static bool Selectable(MenuItemSpec item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind is MenuItemKind.Item or MenuItemKind.Submenu && item.Enabled;
    }

    /// <summary>Moves the highlight by <paramref name="delta"/> over selectable rows, wrapping; keeps it when there is none.</summary>
    public void Move(int delta)
    {
        var n = Items.Count;
        if (n == 0)
        {
            return;
        }

        var at = Highlight >= 0 ? Highlight : delta > 0 ? -1 : 0;
        for (var step = 0; step < n; step++)
        {
            at = ((at + delta) % n + n) % n;
            if (Selectable(Items[at]))
            {
                Highlight = at;
                return;
            }
        }
    }

    /// <summary>The first (or last) selectable row.</summary>
    public void Edge(bool last)
    {
        var range = Enumerable.Range(0, Items.Count);
        var hit = (last ? range.Reverse() : range).Where(i => Selectable(Items[i])).Select(i => (int?)i).FirstOrDefault();
        if (hit is { } i)
        {
            Highlight = i;
        }
    }

    /// <summary>Type-ahead: the next selectable row whose label starts with the typed letter.</summary>
    public bool TypeAhead(char letter)
    {
        var n = Items.Count;
        for (var step = 1; step <= n; step++)
        {
            var i = (Highlight + step + n) % n;
            var label = Items[i].Label.TrimStart();
            if (Selectable(Items[i]) && label.Length > 0 && char.ToUpperInvariant(label[0]) == char.ToUpperInvariant(letter))
            {
                Highlight = i;
                return true;
            }
        }

        return false;
    }
}
