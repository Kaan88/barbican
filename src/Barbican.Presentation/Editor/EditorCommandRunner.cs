// Mirrors: Rust src/rte/mod.rs on_cmd :707-:790 (formatting, block, align, table and history commands) and handle_key's ctrl table.

using System.Globalization;
using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>Runs the commands the session can run alone; the others (dialogs, clipboard, files, layout) belong to the view.</summary>
public static class EditorCommandRunner
{
    /// <summary>Bulwark's default highlight colour (Rust: #fef08a).</summary>
    public const string DefaultHighlight = "#fef08a";

    /// <summary>The body size the size menu calls "Normal": a chosen 14 or 0 clears the size mark (Rust on_cmd "size").</summary>
    public const int NormalSize = 14;

    /// <summary>Commands that need the view and are therefore not run here.</summary>
    public static bool NeedsView(EditorCommand command) => command is EditorCommand.Link or EditorCommand.LinkOpen or EditorCommand.LinkCopy or EditorCommand.LinkRemove
        or EditorCommand.Cut or EditorCommand.Copy or EditorCommand.Paste or EditorCommand.PastePlain or EditorCommand.Image or EditorCommand.ImageFromUrl
        or EditorCommand.ToggleSource or EditorCommand.ImageSize or EditorCommand.SpellReplace or EditorCommand.SpellAdd or EditorCommand.SpellIgnore;

    /// <summary>The commands plain-text mode still takes (no formatting).</summary>
    public static bool AllowedInPlain(EditorCommand command) => command is EditorCommand.Undo or EditorCommand.Redo or EditorCommand.Copy or EditorCommand.Cut
        or EditorCommand.Paste or EditorCommand.PastePlain or EditorCommand.SelectAll or EditorCommand.PlainMode;

    // MA0051: one flat switch from command to session call; splitting it would only scatter the table.
#pragma warning disable MA0051
    /// <summary>Runs <paramref name="command"/>; false when it needs the view or the plain mode refuses it.</summary>
    public static bool Run(IEditorSession session, EditorCommand command, string? argument = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (NeedsView(command) || (session.Plain && !AllowedInPlain(command)))
        {
            return false;
        }

        switch (command)
        {
            case EditorCommand.Bold:
                session.ToggleMark(Mark.Bold);
                break;
            case EditorCommand.Italic:
                session.ToggleMark(Mark.Italic);
                break;
            case EditorCommand.Underline:
                session.ToggleMark(Mark.Underline);
                break;
            case EditorCommand.Strike:
                session.ToggleMark(Mark.Strike);
                break;
            case EditorCommand.Code:
                session.ToggleMark(Mark.Code);
                break;
            case EditorCommand.Highlight:
                session.SetHighlight(HighlightFor(session.CurrentMarks().Highlight is not null, argument));
                break;
            case EditorCommand.Color:
                session.SetColor(string.IsNullOrEmpty(argument) ? null : argument);
                break;
            case EditorCommand.Size:
                session.SetSize(int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var px) && px is not (0 or NormalSize) ? px : null);
                break;
            case EditorCommand.Paragraph:
                session.SetBlock(new BlockKind.Para());
                break;
            case EditorCommand.Heading1:
                session.SetBlock(new BlockKind.Heading(1));
                break;
            case EditorCommand.Heading2:
                session.SetBlock(new BlockKind.Heading(2));
                break;
            case EditorCommand.Heading3:
                session.SetBlock(new BlockKind.Heading(3));
                break;
            case EditorCommand.BulletList:
                session.SetBlock(new BlockKind.Bullet());
                break;
            case EditorCommand.NumberedList:
                session.SetBlock(new BlockKind.Ordered());
                break;
            case EditorCommand.Quote:
                session.SetBlock(new BlockKind.Quote());
                break;
            case EditorCommand.CodeBlock:
                session.SetBlock(new BlockKind.CodeBlock());
                break;
            case EditorCommand.AlignLeft:
                session.SetAlign(Align.Left);
                break;
            case EditorCommand.AlignCenter:
                session.SetAlign(Align.Center);
                break;
            case EditorCommand.AlignRight:
                session.SetAlign(Align.Right);
                break;
            case EditorCommand.AlignJustify:
                session.SetAlign(Align.Justify);
                break;
            case EditorCommand.Indent:
                session.Indent(1);
                break;
            case EditorCommand.Outdent:
                session.Indent(-1);
                break;
            case EditorCommand.ClearFormat:
                session.ClearFormat();
                break;
            case EditorCommand.Undo:
                session.Undo();
                break;
            case EditorCommand.Redo:
                session.Redo();
                break;
            case EditorCommand.Rule:
                session.InsertRule();
                break;
            case EditorCommand.Table:
                var (rows, cols) = ParseGrid(argument);
                session.InsertTable(rows, cols);
                break;
            case EditorCommand.TableRowAbove:
                session.ApplyTable(TableOp.RowAbove);
                break;
            case EditorCommand.TableRowBelow:
                session.ApplyTable(TableOp.RowBelow);
                break;
            case EditorCommand.TableColLeft:
                session.ApplyTable(TableOp.ColLeft);
                break;
            case EditorCommand.TableColRight:
                session.ApplyTable(TableOp.ColRight);
                break;
            case EditorCommand.TableDelRow:
                session.ApplyTable(TableOp.DelRow);
                break;
            case EditorCommand.TableDelCol:
                session.ApplyTable(TableOp.DelCol);
                break;
            case EditorCommand.TableToggleHeader:
                session.ApplyTable(TableOp.ToggleHeader);
                break;
            case EditorCommand.TableDelete:
                session.ApplyTable(TableOp.DelTable);
                break;
            case EditorCommand.SelectAll:
                session.SelectAll();
                break;
            case EditorCommand.PlainMode:
                SwitchToPlain(session);
                break;
            default:
                return false;
        }

        return true;
    }

#pragma warning restore MA0051

    /// <summary>"3x4" to (3, 4); anything else is Rust's default 3 by 3.</summary>
    public static (int Rows, int Cols) ParseGrid(string? argument)
    {
        var parts = (argument ?? string.Empty).Split('x');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r > 0
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c > 0
            ? (r, c)
            : (3, 3);
    }

    private static string? HighlightFor(bool on, string? argument) =>
        string.IsNullOrEmpty(argument) ? (on ? null : DefaultHighlight) : string.Equals(argument, "none", StringComparison.Ordinal) ? null : argument;

    /// <summary>Switching one message to plain text drops its formatting (Bulwark togglePlainTextMode; Rust "plain-mode").</summary>
    private static void SwitchToPlain(IEditorSession session)
    {
        var text = DocHtml.ToText(session.Doc);
        session.Plain = true;
        var doc = new Doc(DocHtml.ImportText(text));
        doc.Fix();
        session.ReplaceDoc(doc, undo: true);
        session.SetCaret(doc.End);
    }
}
