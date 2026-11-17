// Mirrors: Rust src/rte/mod.rs handle_key :536-:688 (the ctrl table); spec/menus.md MENU-RTE shortcuts; Bulwark components/email/email-composer.tsx (TipTap keymaps).
// Ctrl+Alt+digit and Ctrl+Alt+C are the menu's heading and code block shortcuts (Rust tested them behind its AltGr guard, where they could not fire).

namespace Barbican.Presentation.Editor;

/// <summary>The editing shortcuts as a pure table: key and mode to command. Movement, deletion, Enter, Tab and Escape are the controller's.</summary>
public static class EditorKeyMap
{
    private static readonly HashSet<EditorCommand> PlainModeCommands =
    [
        EditorCommand.SelectAll, EditorCommand.Undo, EditorCommand.Redo, EditorCommand.Copy, EditorCommand.Cut, EditorCommand.Paste, EditorCommand.PastePlain,
    ];

    /// <summary>The command a chord runs, or null. <paramref name="plain"/>: plain-text mode ignores formatting shortcuts (they are consumed: <paramref name="swallowed"/>).</summary>
    public static EditorCommand? Resolve(EditorKey key, bool plain, out bool swallowed)
    {
        swallowed = false;
        var command = Lookup(key);
        if (command is null)
        {
            return null;
        }

        if (plain && !PlainModeCommands.Contains(command.Value))
        {
            swallowed = true;
            return null;
        }

        return command;
    }

    /// <summary>The shortcut text of a command for menus and tooltips (Windows wording), or null.</summary>
    public static string? Hint(EditorCommand command) => command switch
    {
        EditorCommand.Bold => "Ctrl+B",
        EditorCommand.Italic => "Ctrl+I",
        EditorCommand.Underline => "Ctrl+U",
        EditorCommand.Strike => "Ctrl+Shift+S",
        EditorCommand.Code => "Ctrl+E",
        EditorCommand.Highlight => "Ctrl+Shift+H",
        EditorCommand.ClearFormat => "Ctrl+\\",
        EditorCommand.Paragraph => "Ctrl+Alt+0",
        EditorCommand.Heading1 => "Ctrl+Alt+1",
        EditorCommand.Heading2 => "Ctrl+Alt+2",
        EditorCommand.Heading3 => "Ctrl+Alt+3",
        EditorCommand.BulletList => "Ctrl+Shift+8",
        EditorCommand.NumberedList => "Ctrl+Shift+7",
        EditorCommand.Quote => "Ctrl+Shift+B",
        EditorCommand.CodeBlock => "Ctrl+Alt+C",
        EditorCommand.AlignLeft => "Ctrl+Shift+L",
        EditorCommand.AlignCenter => "Ctrl+Shift+E",
        EditorCommand.AlignRight => "Ctrl+Shift+R",
        EditorCommand.AlignJustify => "Ctrl+Shift+J",
        EditorCommand.Link => "Ctrl+K",
        EditorCommand.Undo => "Ctrl+Z",
        EditorCommand.Redo => "Ctrl+Y",
        EditorCommand.Cut => "Ctrl+X",
        EditorCommand.Copy => "Ctrl+C",
        EditorCommand.Paste => "Ctrl+V",
        EditorCommand.PastePlain => "Ctrl+Shift+V",
        EditorCommand.SelectAll => "Ctrl+A",
        _ => null,
    };

    private static EditorCommand? Lookup(EditorKey k)
    {
        if (k.Name is "Insert")
        {
            return k.Shift && !k.Ctrl ? EditorCommand.Paste : k.Ctrl && !k.Shift ? EditorCommand.Copy : null;
        }

        if (k.Name is "Delete" && k.Shift && !k.Ctrl)
        {
            return EditorCommand.Cut;
        }

        if (!k.Ctrl)
        {
            return null;
        }

        var name = k.Name.ToUpperInvariant();
        if (k.Alt)
        {
            return !k.Shift ? name switch
            {
                "0" => EditorCommand.Paragraph,
                "1" => EditorCommand.Heading1,
                "2" => EditorCommand.Heading2,
                "3" => EditorCommand.Heading3,
                "C" => EditorCommand.CodeBlock,
                _ => null,
            }
            : null;
        }

        return k.Shift ? Shifted(name) : Plain(name);
    }

    private static EditorCommand? Plain(string name) => name switch
    {
        "A" => EditorCommand.SelectAll,
        "B" => EditorCommand.Bold,
        "I" => EditorCommand.Italic,
        "U" => EditorCommand.Underline,
        "E" => EditorCommand.Code,
        "K" => EditorCommand.Link,
        "Z" => EditorCommand.Undo,
        "Y" => EditorCommand.Redo,
        "C" => EditorCommand.Copy,
        "X" => EditorCommand.Cut,
        "V" => EditorCommand.Paste,
        "\\" or " " => EditorCommand.ClearFormat,
        "]" => EditorCommand.Indent,
        "[" => EditorCommand.Outdent,
        _ => null,
    };

    private static EditorCommand? Shifted(string name) => name switch
    {
        "X" or "S" => EditorCommand.Strike,
        "H" => EditorCommand.Highlight,
        "Z" => EditorCommand.Redo,
        "V" => EditorCommand.PastePlain,
        "7" or "&" => EditorCommand.NumberedList,
        "8" or "*" => EditorCommand.BulletList,
        "B" => EditorCommand.Quote,
        "L" => EditorCommand.AlignLeft,
        "E" => EditorCommand.AlignCenter,
        "R" => EditorCommand.AlignRight,
        "J" => EditorCommand.AlignJustify,
        _ => null,
    };
}
