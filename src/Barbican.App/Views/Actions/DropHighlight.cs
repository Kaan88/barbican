// Mirrors: Bulwark components/layout/sidebar.tsx:540 (a valid drop target under the pointer: bg-primary/20 ring-2 ring-primary ring-inset) and
// lib/folder-tree-dnd.ts:41-134 (a folder dragged onto the top or bottom quarter of a row lands before or after it: a line, not a ring). Owner actions.
// M2 hotfix 2: the user could not see which row a drop would land on. One row at a time carries the mark; it is taken off on leave, drop and drag end.

using Barbican.Core.DragDrop;

namespace Barbican.App.Views.Actions;

/// <summary>The drop-target mark on the folder or tag row under the pointer.</summary>
public static class DropHighlight
{
    private static FrameworkElement? _on;
    private static (Brush? Background, Brush? Border, Thickness Thickness) _saved;

    /// <summary>Marks <paramref name="row"/> (a ListViewItem or a row Panel): a ring and a soft accent fill, or a line above or below for a folder reorder.</summary>
    public static void Show(FrameworkElement row, DropZone zone, bool line)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!ReferenceEquals(_on, row))
        {
            Clear();
            _on = row;
            _saved = Read(row);
        }

        var accent = Accent();
        var soft = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        var ring = new SolidColorBrush(accent);
        var (fill, thickness) = !line ? (soft, new Thickness(2)) : zone switch
        {
            DropZone.Before => (_saved.Background, new Thickness(0, 2, 0, 0)),
            DropZone.After => (_saved.Background, new Thickness(0, 0, 0, 2)),
            _ => (soft, new Thickness(2)),
        };
        Write(row, fill, ring, thickness);
    }

    /// <summary>Takes the mark off the row that has it.</summary>
    public static void Clear()
    {
        if (_on is { } row)
        {
            Write(row, _saved.Background, _saved.Border, _saved.Thickness);
        }

        _on = null;
    }

    /// <summary>Takes the mark off <paramref name="row"/> if it has it (the pointer left that row).</summary>
    public static void Clear(FrameworkElement row)
    {
        if (ReferenceEquals(_on, row))
        {
            Clear();
        }
    }

    private static Windows.UI.Color Accent() =>
        Application.Current.Resources.TryGetValue("Accent", out var b) && b is SolidColorBrush s ? s.Color : Windows.UI.Color.FromArgb(0xFF, 0x3B, 0x6E, 0xF5);

    private static (Brush?, Brush?, Thickness) Read(FrameworkElement row) => row switch
    {
        Control c => (c.Background, c.BorderBrush, c.BorderThickness),
        Grid g => (g.Background, g.BorderBrush, g.BorderThickness),
        Border b => (b.Background, b.BorderBrush, b.BorderThickness),
        _ => (null, null, default),
    };

    private static void Write(FrameworkElement row, Brush? background, Brush? border, Thickness thickness)
    {
        switch (row)
        {
            case Control c:
                c.Background = background;
                c.BorderBrush = border;
                c.BorderThickness = thickness;
                break;
            case Grid g:
                g.Background = background;
                g.BorderBrush = border;
                g.BorderThickness = thickness;
                break;
            case Border b:
                b.Background = background;
                b.BorderBrush = border;
                b.BorderThickness = thickness;
                break;
            default:
                break;
        }
    }
}
