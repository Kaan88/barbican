// Mirrors: spec/menus.md MENU-RTE (item by item); Rust ctxmenu.rs rte menu (the composer's editor), src/rte/mod.rs ctx_info :332.

using Barbican.Core.Editor;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Editor;

/// <summary>The rows of the editor's context menu and its flyouts, evaluated when the menu opens.</summary>
public static class EditorMenus
{
    public const string Menu = "MENU-RTE";
    public const string Format = "MENU-RTE>format";
    public const string Paragraph = "MENU-RTE>paragraph";
    public const string Table = "MENU-RTE>table";
    public const string Rewrite = "MENU-RTE>rewrite";

    /// <summary>How many providers <see cref="Providers"/> returns.</summary>
    public const int ProviderCount = 5;

    /// <summary>The spelling suggestions shown at most (spec: up to 5).</summary>
    public const int MaxSuggestions = 5;

    /// <summary>The rewrite actions of the assistant flyout, in menu order.</summary>
    public static readonly IReadOnlyList<(string Label, string Action)> RewriteActions =
    [
        ("Improve", "improve"), ("Shorten", "shorten"), ("Formal", "formal"), ("Casual", "casual"), ("Fix spelling", "fix-spelling"), ("Translate", "translate"),
    ];

    public static IReadOnlyList<IMenuProvider> Providers(ActiveEditor active)
    {
        ArgumentNullException.ThrowIfNull(active);
        return
        [
            new DelegateMenuProvider(Menu, _ => Main(active.Current)),
            new DelegateMenuProvider(Format, _ => FormatRows(active.Current)),
            new DelegateMenuProvider(Paragraph, _ => ParagraphRows(active.Current)),
            new DelegateMenuProvider(Table, _ => TableRows(active.Current)),
            new DelegateMenuProvider(Rewrite, _ => RewriteRows(active.Current)),
        ];
    }

    // MA0051: the menu is one list in spec order (spec/menus.md MENU-RTE); splitting it would hide that order.
#pragma warning disable MA0051
    public static IReadOnlyList<MenuItemSpec> Main(IEditorSurface? editor)
    {
        if (editor is null)
        {
            return [Note("No editor")];
        }

        var t = editor.Toolbar;
        var rows = new List<MenuItemSpec>();
        if (editor.ContextWord is { } word)
        {
            var suggestions = editor.Spell.Suggestions(word.Text).Take(MaxSuggestions).ToList();
            if (suggestions.Count == 0)
            {
                rows.Add(Note("No suggestions"));
            }

            rows.AddRange(suggestions.Select(s => Item(string.Empty, s, EditorCommandIds.For(EditorCommand.SpellReplace), argument: s)));
            rows.Add(Item(string.Empty, $"Add \"{word.Text}\" to dictionary", EditorCommandIds.For(EditorCommand.SpellAdd)));
            rows.Add(Item(string.Empty, "Ignore", EditorCommandIds.For(EditorCommand.SpellIgnore)));
            rows.Add(Sep());
        }

        rows.Add(Row(EditorCommand.Cut, "Cut", t.HasSelection));
        rows.Add(Row(EditorCommand.Copy, "Copy", t.HasSelection));
        rows.Add(Row(EditorCommand.Paste, "Paste"));
        rows.Add(Row(EditorCommand.PastePlain, "Paste as plain text"));
        rows.Add(Sep());
        rows.Add(Row(EditorCommand.SelectAll, "Select all"));
        rows.Add(Sep());
        rows.Add(Sub(string.Empty, "Format", Format, !editor.Plain));
        rows.Add(Sub(string.Empty, "Paragraph", Paragraph, !editor.Plain));
        if (t.InTable)
        {
            rows.Add(Sub(string.Empty, "Table", Table));
        }

        rows.Add(Sep());
        if (t.Link is not null)
        {
            rows.Add(Row(EditorCommand.Link, "Edit link…"));
            rows.Add(Row(EditorCommand.LinkOpen, "Open link"));
            rows.Add(Row(EditorCommand.LinkCopy, "Copy link address"));
            rows.Add(Row(EditorCommand.LinkRemove, "Remove link"));
        }
        else
        {
            rows.Add(Row(EditorCommand.Link, "Insert link…", !t.OnImage && !editor.Plain));
        }

        if (t.OnImage)
        {
            rows.Add(Sep());
            rows.Add(Item(string.Empty, "Original image size", EditorCommandIds.For(EditorCommand.ImageSize), argument: "0"));
            rows.Add(Item(string.Empty, "Small (240 px)", EditorCommandIds.For(EditorCommand.ImageSize), argument: "240"));
            rows.Add(Item(string.Empty, "Medium (480 px)", EditorCommandIds.For(EditorCommand.ImageSize), argument: "480"));
        }

        rows.Add(Sep());
        rows.Add(Sub(string.Empty, t.HasSelection ? "Rewrite selection with AI" : "Rewrite with AI", Rewrite, editor.Rewrite.IsAvailable));
        rows.Add(Sep());
        rows.Add(Row(EditorCommand.Undo, "Undo", t.CanUndo));
        rows.Add(Row(EditorCommand.Redo, "Redo", t.CanRedo));
        return rows;
    }

#pragma warning restore MA0051

    public static IReadOnlyList<MenuItemSpec> FormatRows(IEditorSurface? editor)
    {
        var t = editor?.Toolbar ?? new ToolbarState();
        return
        [
            Row(EditorCommand.Bold, "Bold", check: t.Bold),
            Row(EditorCommand.Italic, "Italic", check: t.Italic),
            Row(EditorCommand.Underline, "Underline", check: t.Underline),
            Row(EditorCommand.Strike, "Strikethrough", check: t.Strike),
            Row(EditorCommand.Code, "Inline code", check: t.Code),
            Row(EditorCommand.Highlight, "Highlight", check: t.Highlight),
            Sep(),
            Row(EditorCommand.ClearFormat, "Clear formatting"),
        ];
    }

    public static IReadOnlyList<MenuItemSpec> ParagraphRows(IEditorSurface? editor)
    {
        var t = editor?.Toolbar ?? new ToolbarState();
        return
        [
            Row(EditorCommand.Paragraph, "Normal text", check: t.Block is BlockKind.Para),
            Row(EditorCommand.Heading1, "Heading 1", check: t.Block is BlockKind.Heading { Level: 1 }),
            Row(EditorCommand.Heading2, "Heading 2", check: t.Block is BlockKind.Heading { Level: 2 }),
            Row(EditorCommand.Heading3, "Heading 3", check: t.Block is BlockKind.Heading { Level: 3 }),
            Sep(),
            Row(EditorCommand.BulletList, "Bulleted list", check: t.Block is BlockKind.Bullet),
            Row(EditorCommand.NumberedList, "Numbered list", check: t.Block is BlockKind.Ordered),
            Row(EditorCommand.Quote, "Quote", check: t.Block is BlockKind.Quote),
            Row(EditorCommand.CodeBlock, "Code block", check: t.Block is BlockKind.CodeBlock),
            Sep(),
            Row(EditorCommand.AlignLeft, "Align left", check: t.Align == Align.Left),
            Row(EditorCommand.AlignCenter, "Center", check: t.Align == Align.Center),
            Row(EditorCommand.AlignRight, "Align right", check: t.Align == Align.Right),
            Row(EditorCommand.AlignJustify, "Justify", check: t.Align == Align.Justify),
        ];
    }

    public static IReadOnlyList<MenuItemSpec> TableRows(IEditorSurface? editor)
    {
        _ = editor;
        return
        [
            Row(EditorCommand.TableRowAbove, "Add row above"),
            Row(EditorCommand.TableRowBelow, "Add row below"),
            Row(EditorCommand.TableColLeft, "Add column left"),
            Row(EditorCommand.TableColRight, "Add column right"),
            Sep(),
            Row(EditorCommand.TableDelRow, "Delete row"),
            Row(EditorCommand.TableDelCol, "Delete column"),
            Row(EditorCommand.TableToggleHeader, "Toggle header row"),
            Sep(),
            Danger(string.Empty, "Delete table", EditorCommandIds.For(EditorCommand.TableDelete)),
        ];
    }

    public static IReadOnlyList<MenuItemSpec> RewriteRows(IEditorSurface? editor)
    {
        var on = editor?.Rewrite.IsAvailable ?? false;
        return [.. RewriteActions.Select(a => Item(string.Empty, a.Label, "rte.rewrite", argument: a.Action, enabled: on))];
    }

    private static MenuItemSpec Row(EditorCommand command, string label, bool enabled = true, bool? check = null) =>
        Item(string.Empty, label, EditorCommandIds.For(command), EditorKeyMap.Hint(command), enabled: enabled, check: check);
}
