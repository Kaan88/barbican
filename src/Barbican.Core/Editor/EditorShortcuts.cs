// Mirrors: Bulwark components/email/rich-text-editor.tsx:539-600 (StarterKit with heading levels 1 and 2, Underline, TextAlign for heading and paragraph); the keymaps are TipTap's defaults
// (@tiptap/extension-bold, -italic, -underline, -strike, -code, -bullet-list, -ordered-list, -blockquote, -code-block, -heading, -paragraph, -text-align, -history, -hard-break, -list-item).
// Rust src/rte/mod.rs on_key (Tab in a table cell moves between cells). Decision D-M3B-07 (spec/decisions.md).

using System.Globalization;

namespace Barbican.Core.Editor;

/// <summary>TipTap's keyboard shortcuts as commands. The view normalises a key event to a chord and calls <see cref="TryExecute"/>; everything else (arrows, Home/End, clipboard, Mod-k) stays in the view.</summary>
public static class EditorShortcuts
{
    /// <summary>Every shortcut, in the order TipTap documents them.</summary>
    public static IReadOnlyList<EditorShortcut> All { get; } =
    [
        Fmt("Mod-b", "toggleBold", s => s.ToggleMark(Mark.Bold)),
        Fmt("Mod-i", "toggleItalic", s => s.ToggleMark(Mark.Italic)),
        Fmt("Mod-u", "toggleUnderline", s => s.ToggleMark(Mark.Underline)),
        Fmt("Mod-Shift-s", "toggleStrike", s => s.ToggleMark(Mark.Strike)),
        Fmt("Mod-e", "toggleCode", s => s.ToggleMark(Mark.Code)),
        Fmt("Mod-Shift-8", "toggleBulletList", s => s.SetBlock(new BlockKind.Bullet())),
        Fmt("Mod-Shift-7", "toggleOrderedList", s => s.SetBlock(new BlockKind.Ordered())),
        Fmt("Mod-Shift-b", "toggleBlockquote", s => s.SetBlock(new BlockKind.Quote())),
        Fmt("Mod-Alt-c", "toggleCodeBlock", s => s.SetBlock(new BlockKind.CodeBlock())),
        Fmt("Mod-Alt-1", "toggleHeading1", s => s.SetBlock(new BlockKind.Heading(1))),
        Fmt("Mod-Alt-2", "toggleHeading2", s => s.SetBlock(new BlockKind.Heading(2))),
        Fmt("Mod-Alt-0", "setParagraph", s => s.SetBlock(new BlockKind.Para())),
        Fmt("Mod-Shift-l", "setTextAlignLeft", s => s.SetAlign(Align.Left)),
        Fmt("Mod-Shift-e", "setTextAlignCenter", s => s.SetAlign(Align.Center)),
        Fmt("Mod-Shift-r", "setTextAlignRight", s => s.SetAlign(Align.Right)),
        Fmt("Mod-Shift-j", "setTextAlignJustify", s => s.SetAlign(Align.Justify)),
        new EditorShortcut("Tab", "sinkListItem", Formatting: true, s => s.CellTab(back: false) || s.Indent(1)),
        new EditorShortcut("Shift-Tab", "liftListItem", Formatting: true, s => s.CellTab(back: true) || s.Indent(-1)),
        Plain("Mod-z", "undo", s => { s.Undo(); }),
        Plain("Mod-Shift-z", "redo", s => { s.Redo(); }),
        Plain("Mod-y", "redo", s => { s.Redo(); }),
        Plain("Mod-a", "selectAll", s => s.SelectAll()),
        Plain("Enter", "splitBlock", s => s.Enter(shift: false)),
        Plain("Shift-Enter", "setHardBreak", s => s.Enter(shift: true)),
        Plain("Mod-Enter", "setHardBreak", s => s.Enter(shift: true)),
        Plain("Backspace", "deleteBackward", s => s.Backspace(word: false)),
        Plain("Mod-Backspace", "deleteWordBackward", s => s.Backspace(word: true)),
        Plain("Delete", "deleteForward", s => s.DeleteForward(word: false)),
        Plain("Mod-Delete", "deleteWordForward", s => s.DeleteForward(word: true)),
    ];

    private static readonly Dictionary<string, EditorShortcut> ByChord = BuildIndex();

    /// <summary>The shortcut for a chord (modifier order and key case do not matter), or null.</summary>
    public static EditorShortcut? Find(string chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        return ByChord.GetValueOrDefault(Normalize(chord));
    }

    /// <summary>Runs the shortcut of <paramref name="chord"/> on the session. True when the key is consumed: the chord is known and the command ran, or it is a formatting chord in plain-text mode (swallowed).</summary>
    public static bool TryExecute(IEditorSession session, string chord)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Find(chord) is not { } shortcut)
        {
            return false;
        }

        if (shortcut.Formatting && session.Plain)
        {
            return !shortcut.Chord.EndsWith("Tab", StringComparison.Ordinal);
        }

        return shortcut.Run(session);
    }

    /// <summary>Canonical form: lower case, modifiers in the order mod, alt, shift, joined by dashes, then the key.</summary>
    public static string Normalize(string chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        bool mod = false, alt = false, shift = false;
        var key = "";
        foreach (var part in chord.Split('-', StringSplitOptions.None))
        {
            switch (part.ToLowerInvariant())
            {
                case "mod" or "ctrl" or "control" or "cmd" or "meta":
                    mod = true;
                    break;
                case "alt" or "option":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "":
                    key = "-"; // the minus key itself ("Mod--")
                    break;
                default:
                    key = part.ToLowerInvariant();
                    break;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(mod ? "mod-" : "")}{(alt ? "alt-" : "")}{(shift ? "shift-" : "")}{key}");
    }

    private static EditorShortcut Fmt(string chord, string command, Action<IEditorSession> run) =>
        new(chord, command, Formatting: true, s =>
        {
            run(s);
            return true;
        });

    private static EditorShortcut Plain(string chord, string command, Action<IEditorSession> run) =>
        new(chord, command, Formatting: false, s =>
        {
            run(s);
            return true;
        });

    private static Dictionary<string, EditorShortcut> BuildIndex()
    {
        var d = new Dictionary<string, EditorShortcut>(StringComparer.Ordinal);
        foreach (var s in All)
        {
            d[Normalize(s.Chord)] = s;
        }

        return d;
    }
}
