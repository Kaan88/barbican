// Mirrors: Rust src/rte/mod.rs handle_key :536-:640 (movement by grapheme, word, visual line, goal column), on_pointer :640 (click counting, drag, word drag, quote header),
// right_down :381, resize :399 (image corner handles); Bulwark components/email/rich-text-editor.tsx (selection and caret behaviour, B-RTE-029 Escape).

using Barbican.Core.Editor;

namespace Barbican.Editor.Layout.Input;

/// <summary>
/// Caret, selection and pointer logic over a session and its layout: everything that needs line geometry. The control feeds it key and pointer events in document
/// coordinates; it needs no UI framework, so it is tested headless.
/// </summary>
public sealed class EditorController
{
    private const double MultiClickMs = 450;
    private const float MultiClickSlop = 5f;
    private const int PageLines = 15;
    private const float MinImageWidth = 24f;

    private readonly TimeProvider _time;
    private (DateTimeOffset At, float X, float Y, int Count)? _lastClick;
    private Pos? _dragAnchor;
    private bool _dragByWord;
    private (int Block, bool Left, float X0, float W0, float H0, bool First)? _resize;

    public EditorController(IEditorSession session, DocLayout layout, TimeProvider? time = null)
    {
        Session = session;
        Layout = layout;
        _time = time ?? TimeProvider.System;
    }

    public IEditorSession Session { get; }

    public DocLayout Layout { get; }

    /// <summary>The caret sits at the end of a wrapped line (the "upstream" affinity of the text engine).</summary>
    public bool Upstream { get; private set; }

    public bool Dragging => _dragAnchor is not null;

    public bool Resizing => _resize is not null;

    /// <summary>Lays out the document at <paramref name="width"/>.</summary>
    public void Build(float width) => Layout.Build(Session.Doc, width);

    public CaretRect Caret() => Layout.Caret(Session.Sel.Focus, Upstream);

    /// <summary>Inserts typed or composed text at the caret (control characters are dropped; Rust: the printable filter).</summary>
    public bool TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var printable = new string([.. text.Where(c => !char.IsControl(c) || c is '\n')]);
        if (printable.Length == 0)
        {
            return false;
        }

        Upstream = false;
        Session.GoalX = null;
        Session.InsertText(printable);
        return true;
    }

    /// <summary>A navigation or deletion key; true when handled (the control then repaints and scrolls to the caret).</summary>
    public bool Key(NavKey key, bool ctrl, bool shift, bool alt)
    {
        if (key is NavKey.Left or NavKey.Right or NavKey.Up or NavKey.Down or NavKey.Home or NavKey.End or NavKey.PageUp or NavKey.PageDown)
        {
            Move(key, ctrl, shift);
            return true;
        }

        Upstream = false;
        switch (key)
        {
            case NavKey.Backspace:
                Session.GoalX = null;
                Session.Backspace(ctrl);
                return true;
            case NavKey.Delete:
                Session.DeleteForward(ctrl);
                return true;
            case NavKey.Enter:
                if (ctrl)
                {
                    return false;
                }

                Session.Enter(shift);
                return true;
            case NavKey.Tab:
                if (ctrl || alt)
                {
                    return false;
                }

                if (Session.CellTab(shift) || Session.Indent(shift ? -1 : 1))
                {
                    return true;
                }

                if (shift)
                {
                    return false;
                }

                Session.InsertText("\t");
                return true;
            case NavKey.Escape:
                // a selection (and its toolbar) is dismissed first; only a second Escape closes the message (B-RTE-029)
                if (Session.Sel.IsCollapsed)
                {
                    return false;
                }

                Session.SetCaret(Session.Sel.Focus);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Pointer press at a document point. A press on a quoted original's header toggles it; otherwise click, double click (word) and triple click (block),
    /// Shift extends. Returns the position and affinity hit.
    /// </summary>
    public (Pos Pos, bool Upstream) PointerDown(float x, float y, bool shift)
    {
        var (pos, up) = Layout.HitTest(x, y);
        var header = Layout.QuotedHeaderAt(x, y);
        if (header >= 0)
        {
            Session.ToggleQuoted(header);
            Session.SetCaret(new Pos(header, 0));
            _dragAnchor = null;
            return (new Pos(header, 0), false);
        }

        var now = _time.GetUtcNow();
        var count = 1;
        if (_lastClick is { } last && (now - last.At).TotalMilliseconds < MultiClickMs && Math.Abs(last.X - x) < MultiClickSlop && Math.Abs(last.Y - y) < MultiClickSlop)
        {
            count = (last.Count % 3) + 1;
        }

        _lastClick = (now, x, y, count);
        Upstream = up;
        Session.GoalX = null;
        switch (count)
        {
            case 2:
                Session.SelectWord(pos);
                break;
            case 3:
                Session.SelectBlock(pos.B);
                break;
            default:
                if (shift)
                {
                    Session.SelectRange(Session.Sel.Anchor, pos);
                }
                else
                {
                    Session.SetCaret(pos);
                }

                break;
        }

        _dragAnchor = count >= 2 ? Session.Sel.Start : Session.Sel.Anchor;
        _dragByWord = count == 2;
        return (pos, up);
    }

    /// <summary>The pointer moved with the button down: extends the selection (by whole words after a double click).</summary>
    public void PointerDrag(float x, float y)
    {
        if (_dragAnchor is not { } anchor)
        {
            return;
        }

        var (pos, up) = Layout.HitTest(x, y);
        Upstream = up;
        if (!_dragByWord)
        {
            Session.SelectRange(anchor, pos);
            return;
        }

        var saved = Session.Sel;
        Session.SelectWord(pos);
        var word = Session.Sel;
        if (pos < anchor)
        {
            Session.SelectRange(Pos.Max(saved.End, anchor), word.Start);
        }
        else
        {
            Session.SelectRange(anchor, word.End);
        }
    }

    public void PointerUp() => _dragAnchor = null;

    /// <summary>Right button: the caret moves there unless the click is inside the selection (so the menu acts on it). Returns the hit position.</summary>
    public Pos RightDown(float x, float y)
    {
        var (pos, up) = Layout.HitTest(x, y);
        var sel = Session.Sel;
        var inside = !sel.IsCollapsed && pos >= sel.Start && pos <= sel.End;
        if (!inside)
        {
            Upstream = up;
            Session.GoalX = null;
            Session.SetCaret(pos);
        }

        return pos;
    }

    /// <summary>Starts an image resize from a corner handle ("left" corners grow leftwards); the aspect ratio is kept.</summary>
    public bool ResizeBegin(bool leftCorner, float x)
    {
        var b = Session.Sel.Focus.B;
        if (Layout.ImageRect(b) is not { } r)
        {
            return false;
        }

        _resize = (b, leftCorner, x, r.Width, r.Height, true);
        return true;
    }

    /// <summary>Drags the handle to <paramref name="x"/>: one undo step for the whole drag (the first call checkpoints, later calls merge).</summary>
    public bool ResizeMove(float x)
    {
        if (_resize is not { } rz)
        {
            return false;
        }

        var w = NewWidth(rz.W0, rz.Left ? rz.X0 - x : x - rz.X0, Layout.Width);
        var h = MathF.Max(1f, MathF.Round(w * rz.H0 / MathF.Max(rz.W0, 1f)));
        if (!Session.ResizeImage(rz.Block, (int)w, (int)h, rz.First))
        {
            return false;
        }

        _resize = rz with { First = false };
        return true;
    }

    public void ResizeEnd() => _resize = null;

    /// <summary>Width after dragging a corner by <paramref name="dx"/> (at least 24 px, at most the editor width; Rust new_width).</summary>
    public static float NewWidth(float width, float dx, float max) => Math.Clamp(MathF.Round(width + dx), MinImageWidth, MathF.Max(max, MinImageWidth));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '\'' or '-';

    /// <summary>Start of the word before <paramref name="o"/> (skips spaces, then the word; Windows Ctrl+Left).</summary>
    public static int PrevWord(string s, int o)
    {
        var i = Math.Min(o, s.Length);
        while (i > 0 && !IsWordChar(s[i - 1]) && !char.IsWhiteSpace(s[i - 1]))
        {
            i--;
        }

        while (i > 0 && char.IsWhiteSpace(s[i - 1]))
        {
            i--;
        }

        while (i > 0 && IsWordChar(s[i - 1]))
        {
            i--;
        }

        return i;
    }

    /// <summary>Start of the next word after <paramref name="o"/> (skips the rest of this word, then spaces; Windows Ctrl+Right).</summary>
    public static int NextWord(string s, int o)
    {
        var i = Math.Min(o, s.Length);
        if (i < s.Length && IsWordChar(s[i]))
        {
            while (i < s.Length && IsWordChar(s[i]))
            {
                i++;
            }
        }
        else if (i < s.Length && !char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    private (Pos Target, bool Up, float? Goal) MoveLines(Pos f, NavKey key)
    {
        var goal = Session.GoalX ?? Layout.Caret(f, Upstream).X;
        var dir = key is NavKey.Up or NavKey.PageUp ? -1 : 1;
        var n = key is NavKey.PageUp or NavKey.PageDown ? PageLines : 1;
        var (p, pu) = (f, Upstream);
        for (var i = 0; i < n; i++)
        {
            var (q, qu) = Layout.MoveVertical(Session.Doc, p, pu, dir, goal);
            if (q == p)
            {
                break;
            }

            (p, pu) = (q, qu);
        }

        return (p, pu, goal);
    }

    private void Move(NavKey key, bool ctrl, bool shift)
    {
        var doc = Session.Doc;
        var sel = Session.Sel;
        var f = sel.Focus;
        Pos target;
        var up = false;
        float? keep = null;
        if (!shift && !sel.IsCollapsed && key is NavKey.Left or NavKey.Right && !ctrl)
        {
            target = key is NavKey.Left ? sel.Start : sel.End;
        }
        else if (key is NavKey.Left)
        {
            target = f.O == 0
                ? (f.B > 0 ? new Pos(f.B - 1, doc.Blocks[f.B - 1].Length) : f)
                : new Pos(f.B, ctrl ? PrevWord(doc.Blocks[f.B].Text, f.O) : TextOffsets.PrevChar(doc.Blocks[f.B].Text, f.O));
        }
        else if (key is NavKey.Right)
        {
            var len = doc.Blocks[f.B].Length;
            target = f.O >= len
                ? (f.B + 1 < doc.Blocks.Count ? new Pos(f.B + 1, 0) : f)
                : new Pos(f.B, ctrl ? NextWord(doc.Blocks[f.B].Text, f.O) : TextOffsets.NextChar(doc.Blocks[f.B].Text, f.O));
        }
        else if (key is NavKey.Home)
        {
            target = ctrl ? new Pos(0, 0) : Layout.MoveInLine(f, Upstream, end: false) is { } h ? h.Pos : new Pos(f.B, 0);
        }
        else if (key is NavKey.End)
        {
            if (ctrl)
            {
                target = doc.End;
            }
            else if (Layout.MoveInLine(f, Upstream, end: true) is { } e)
            {
                (target, up) = e;
            }
            else
            {
                (target, up) = (new Pos(f.B, doc.Blocks[f.B].Length), true);
            }
        }
        else
        {
            (target, up, keep) = MoveLines(f, key);
        }

        if (shift)
        {
            Session.SelectRange(sel.Anchor, target);
        }
        else
        {
            Session.SetCaret(target);
        }

        Upstream = up;
        Session.GoalX = keep;
    }
}
