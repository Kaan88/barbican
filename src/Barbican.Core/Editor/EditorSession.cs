// Mirrors: Rust src/rte/editor.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap commands and input rules). No RFC or Stalwart counterpart.
// Decisions: spec/decisions.md D-M3B-01 (undo snapshots), D-M3B-03 (plain mode ignores formatting commands, B-RTE-020), D-M3B-04 (UTF-16 offsets, grapheme steps), D-M3B-08 (CR normalised).

using System.Text;

// MA0051: the commands are one-to-one ports of Rust editor.rs bodies; splitting them scatters the order of the checks the Rust oracle defines.
#pragma warning disable MA0051

namespace Barbican.Core.Editor;

/// <summary>
/// Editing commands, input rules and undo on top of <see cref="Doc"/> (port of Rust <c>Editor</c>).
/// Layout-independent; cursor movement that needs line geometry lives in the view. Not thread-safe.
/// </summary>
public sealed class EditorSession : IEditorSession
{
    /// <summary>Undo steps kept (Rust <c>HISTORY</c>).</summary>
    public const int HistoryLimit = 300;

    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(1200);
    private static readonly Mark[] BooleanMarks = [Mark.Bold, Mark.Italic, Mark.Underline, Mark.Strike, Mark.Code];

    private readonly List<(Doc Doc, Sel Sel)> _undo = [];
    private readonly List<(Doc Doc, Sel Sel)> _redo = [];
    private (EditKind Kind, long At)? _last;
    private Marks? _stored;
    private long _version = 1;

    public EditorSession()
        : this(new Doc())
    {
    }

    /// <summary>A session over <paramref name="doc"/>; <paramref name="time"/> times the 1200 ms undo grouping (a fake in tests, the system clock when null).</summary>
    public EditorSession(Doc doc, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        Doc = doc;
        Time = time ?? TimeProvider.System;
        doc.Fix();
    }

    public event EventHandler? Changed;

    public TimeProvider Time { get; }

    public Doc Doc { get; private set; }

    public Sel Sel { get; private set; }

    public Marks? Stored => _stored;

    public float? GoalX { get; set; }

    public long Version => _version;

    public bool Plain { get; set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<ImageData> InlineImages =>
        [.. Doc.Blocks.Select(b => b.Kind).OfType<BlockKind.Image>().Select(i => i.Data).Where(d => d.Cid is not null)];

    private Block FocusBlock => Doc.Blocks[Sel.Focus.B];

    // ------------------------------------------------------------ history

    public void Load(Doc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        doc.Fix();
        Doc = doc;
        Sel = default;
        _stored = null;
        _undo.Clear();
        _redo.Clear();
        _last = null;
        GoalX = null;
        _version++;
        Notify();
    }

    public void Checkpoint(EditKind kind)
    {
        Cp(kind);
        Notify();
    }

    public void BreakGroup() => _last = null;

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var (d, s) = Pop(_undo);
        _redo.Add((Doc, Sel));
        Doc = d;
        Sel = s;
        AfterChange();
        Notify();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var (d, s) = Pop(_redo);
        _undo.Add((Doc, Sel));
        Doc = d;
        Sel = s;
        AfterChange();
        Notify();
        return true;
    }

    // ------------------------------------------------------------ selection

    public void SetCaret(Pos p)
    {
        Sel = Sel.Caret(Doc.Clamp(p));
        _stored = null;
        BreakGroup();
        Notify();
    }

    public void SelectRange(Pos anchor, Pos focus)
    {
        Select(anchor, focus);
        Notify();
    }

    public void SelectAll()
    {
        Select(new Pos(0, 0), Doc.End);
        Notify();
    }

    public string SelectedText() => Sel.IsCollapsed ? "" : Doc.TextRange(Sel.Start, Sel.End);

    public IReadOnlyList<Block> SelectedBlocks() => Sel.IsCollapsed ? [] : Doc.Fragment(Sel.Start, Sel.End);

    /// <summary>Selects the word around <paramref name="p"/> (letters, digits, '_', apostrophe and hyphen; whole graphemes).</summary>
    public void SelectWord(Pos p)
    {
        p = Doc.Clamp(p);
        var t = Doc.Blocks[p.B].Text;
        var a = p.O;
        while (a > 0)
        {
            var q = TextOffsets.PrevChar(t, a);
            if (AllRunes(t, q, a, IsWordRune))
            {
                a = q;
            }
            else
            {
                break;
            }
        }

        var b = p.O;
        while (b < t.Length)
        {
            var q = TextOffsets.NextChar(t, b);
            if (AllRunes(t, b, q, IsWordRune))
            {
                b = q;
            }
            else
            {
                break;
            }
        }

        if (a == b && b < t.Length)
        {
            b = TextOffsets.NextChar(t, b);
        }

        Select(new Pos(p.B, a), new Pos(p.B, b));
        Notify();
    }

    public void SelectBlock(int block)
    {
        var b = Math.Clamp(block, 0, Doc.Blocks.Count - 1);
        Select(new Pos(b, 0), new Pos(b, Doc.Blocks[b].Length));
        Notify();
    }

    // ------------------------------------------------------------ queries

    public Marks CurrentMarks()
    {
        if (_stored is not null)
        {
            return _stored;
        }

        var p = Sel.Focus;
        if (Sel.IsCollapsed)
        {
            return Doc.Blocks[p.B].MarksAt(p.O);
        }

        // a selection: a boolean mark is "on" when every selected run has it
        var (a, b) = (Sel.Start, Sel.End);
        Marks? first = null;
        var all = new[] { true, true, true, true, true };
        for (var i = a.B; i <= b.B; i++)
        {
            var blk = Doc.Blocks[i];
            if (blk.Kind.IsAtomic)
            {
                continue;
            }

            var s = i == a.B ? a.O : 0;
            var e = i == b.B ? b.O : blk.Length;
            foreach (var r in blk.RunsIn(s, e))
            {
                first ??= r.Marks;
                for (var k = 0; k < BooleanMarks.Length; k++)
                {
                    all[k] &= BooleanMarks[k].IsOn(r.Marks);
                }
            }
        }

        var m = first ?? Marks.Plain;
        for (var k = 0; k < BooleanMarks.Length; k++)
        {
            m = BooleanMarks[k].With(m, all[k]);
        }

        return m;
    }

    // ------------------------------------------------------------ text edits

    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        // pasted line breaks are normalised; a bare CR never reaches the model
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var typing = text.EnumerateRunes().Take(2).Count() == 1;
        Cp(typing ? EditKind.Typing : EditKind.Other);
        var marks = CurrentMarks();
        var stored = TakeStored();
        DeleteSelection();
        marks = stored ?? (Sel.IsCollapsed ? Doc.Blocks[Sel.Focus.B].MarksAt(Sel.Focus.O) : marks);

        // a code block or table cell keeps newlines; elsewhere a multi-line text becomes paragraphs
        Pos p;
        if (text.Contains('\n', StringComparison.Ordinal) && FocusBlock.Kind is not (BlockKind.CodeBlock or BlockKind.Cell))
        {
            var blocks = text.Split('\n').Select(l => Block.WithText(new BlockKind.Para(), l, marks)).ToList();
            p = Doc.InsertBlocks(Sel.Focus, blocks);
        }
        else
        {
            p = Doc.InsertText(Sel.Focus, text, marks);
        }

        Sel = Sel.Caret(p);
        GoalX = null;
        if (typing && !Plain && text.Length == 1)
        {
            InputRules(text[0]);
        }

        Notify();
    }

    public void Enter(bool shift)
    {
        EnterCore(shift);
        Notify();
    }

    public void Backspace(bool word)
    {
        BackspaceCore(word);
        Notify();
    }

    public void DeleteForward(bool word)
    {
        DeleteForwardCore(word);
        Notify();
    }

    // ------------------------------------------------------------ formatting

    public void ToggleMark(Mark mark)
    {
        if (Plain)
        {
            return;
        }

        var on = !mark.IsOn(CurrentMarks());
        MapSelectionMarks(m => mark.With(m, on));
        Notify();
    }

    public void SetColor(string? color)
    {
        if (Plain)
        {
            return;
        }

        MapSelectionMarks(m => m with { Color = color });
        Notify();
    }

    public void SetHighlight(string? color)
    {
        if (Plain)
        {
            return;
        }

        MapSelectionMarks(m => m with { Highlight = color });
        Notify();
    }

    public void SetSize(int? size)
    {
        if (Plain)
        {
            return;
        }

        MapSelectionMarks(m => m with { Size = size });
        Notify();
    }

    /// <summary>Toggles a block type for every block of the selection: when all have it already they become paragraphs.</summary>
    public void SetBlock(BlockKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (Plain && kind is not BlockKind.Para)
        {
            return;
        }

        Cp(EditKind.Other);
        var (s, e) = BlockRange();
        var all = Enumerable.Range(s, e - s + 1).Select(i => Doc.Blocks[i]).Where(b => !b.Kind.IsAtomic && !b.Kind.IsCell).All(b => b.Kind == kind);
        for (var i = s; i <= e; i++)
        {
            var b = Doc.Blocks[i];
            if (b.Kind.IsAtomic || b.Kind.IsCell)
            {
                continue;
            }

            if (all)
            {
                b.Kind = new BlockKind.Para();
                b.Indent = 0;
            }
            else
            {
                if (!kind.IsList)
                {
                    b.Indent = 0;
                }

                b.Kind = kind;
            }
        }

        Notify();
    }

    public void SetAlign(Align align)
    {
        if (Plain)
        {
            return;
        }

        Cp(EditKind.Other);
        var (s, e) = BlockRange();
        for (var i = s; i <= e; i++)
        {
            Doc.Blocks[i].Align = align;
        }

        Notify();
    }

    public bool Indent(int delta)
    {
        if (Plain)
        {
            return false;
        }

        var (s, e) = BlockRange();
        if (!Enumerable.Range(s, e - s + 1).Any(i => Doc.Blocks[i].Kind.IsList))
        {
            return false;
        }

        Cp(EditKind.Other);
        for (var i = s; i <= e; i++)
        {
            int? prevLevel = i > 0 && Doc.Blocks[i - 1].Kind.IsList ? Doc.Blocks[i - 1].Indent : null;
            var b = Doc.Blocks[i];
            if (!b.Kind.IsList)
            {
                continue;
            }

            if (delta > 0)
            {
                // only one level deeper than the previous item
                if (prevLevel is int l && b.Indent <= l && b.Indent < 6)
                {
                    b.Indent++;
                }
            }
            else if (b.Indent > 0)
            {
                b.Indent--;
            }
            else
            {
                b.Kind = new BlockKind.Para();
            }
        }

        Notify();
        return true;
    }

    public void ClearFormat()
    {
        if (Plain)
        {
            return;
        }

        Cp(EditKind.Other);
        if (Sel.IsCollapsed)
        {
            _stored = Marks.Plain;
        }
        else
        {
            ForEachSelectedRange((blk, s, e) => blk.MapMarks(s, e, m => Marks.Plain with { Link = m.Link }));
        }

        var (bs, be) = BlockRange();
        for (var i = bs; i <= be; i++)
        {
            var b = Doc.Blocks[i];
            if (b.Kind is BlockKind.Heading or BlockKind.Quote or BlockKind.CodeBlock)
            {
                b.Kind = new BlockKind.Para();
            }

            b.Align = Align.Left;
        }

        Notify();
    }

    // ------------------------------------------------------------ links

    /// <summary>The link run around the caret: block, start, end (UTF-16), url.</summary>
    public (int Block, int Start, int End, string Url)? LinkRange()
    {
        var p = Sel.Focus;
        var blk = Doc.Blocks[p.B];
        var spans = new List<(int S, int E, string Url)>();
        var acc = 0;
        foreach (var r in blk.Runs)
        {
            var (s, e) = (acc, acc + r.Text.Length);
            acc = e;
            if (r.Marks.Link is not { } l)
            {
                continue;
            }

            if (spans.Count > 0 && spans[^1].E == s && string.Equals(spans[^1].Url, l, StringComparison.Ordinal))
            {
                spans[^1] = spans[^1] with { E = e };
            }
            else
            {
                spans.Add((s, e, l));
            }
        }

        foreach (var (s, e, url) in spans)
        {
            if ((s < p.O && p.O <= e) || (s == 0 && p.O == 0 && e > 0))
            {
                return (p.B, s, e, url);
            }
        }

        return null;
    }

    public string? CurrentLink() => Sel.IsCollapsed ? LinkRange()?.Url : CurrentMarks().Link;

    /// <summary>Normalises what the user typed: scheme kept, a bare address becomes mailto:, a bare host gets https:// (Rust <c>set_link</c>); null for an empty url.</summary>
    public static string? NormalizeUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        url = url.Trim();
        if (url.Length == 0)
        {
            return null;
        }

        if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.Ordinal) || url.StartsWith("tel:", StringComparison.Ordinal))
        {
            return url;
        }

        return url.Contains('@', StringComparison.Ordinal) && !url.Contains('/', StringComparison.Ordinal) ? "mailto:" + url : "https://" + url;
    }

    public void SetLink(string url, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Plain)
        {
            return;
        }

        var link = NormalizeUrl(url);
        if (Sel.IsCollapsed)
        {
            if (LinkRange() is { } lr)
            {
                var (b, s, e, _) = lr;
                Cp(EditKind.Other);
                var blk = Doc.Blocks[b];
                if (text.Length > 0 && !string.Equals(text, blk.Text[s..e], StringComparison.Ordinal))
                {
                    var m = blk.MarksAt(Math.Min(s + 1, blk.Length));
                    blk.Cut(s, e);
                    blk.Insert(s, text, m);
                    blk.MapMarks(s, s + text.Length, x => x with { Link = link });
                    Sel = Sel.Caret(new Pos(b, s + text.Length));
                }
                else
                {
                    blk.MapMarks(s, e, x => x with { Link = link });
                }

                Notify();
                return;
            }

            if (link is null)
            {
                return;
            }

            var t = text.Length == 0 ? (link.StartsWith("mailto:", StringComparison.Ordinal) ? link["mailto:".Length..] : link) : text;
            Cp(EditKind.Other);
            var marks = CurrentMarks() with { Link = link };
            var p = Doc.InsertText(Sel.Focus, t, marks);
            Sel = Sel.Caret(p);
            _stored = marks with { Link = null };
            Notify();
            return;
        }

        MapSelectionMarks(m => m with { Link = link });
        Notify();
    }

    // ------------------------------------------------------------ blocks

    public void InsertBlocks(IReadOnlyList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count == 0)
        {
            return;
        }

        Cp(EditKind.Other);
        DeleteSelection();
        var p = Doc.InsertBlocks(Sel.Focus, blocks);
        Sel = Sel.Caret(p);
        Notify();
    }

    public void InsertImage(ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (Plain)
        {
            return;
        }

        Cp(EditKind.Other);
        DeleteSelection();
        var p = Sel.Focus;
        var cur = Doc.Blocks[p.B];
        int at;
        if (cur.Kind.IsCell)
        {
            // images go after the table
            at = Doc.TableRange(p.B)?.End ?? (p.B + 1);
        }
        else if (cur.IsEmpty && !cur.Kind.IsAtomic)
        {
            Doc.Blocks.RemoveAt(p.B);
            at = p.B;
        }
        else if (p.O == 0 && !cur.Kind.IsAtomic)
        {
            at = p.B;
        }
        else
        {
            at = Doc.Split(p).B;
        }

        Doc.Blocks.Insert(at, new Block(new BlockKind.Image(image)));

        // always leave an empty paragraph after the image to keep typing in
        var nextEmpty = at + 1 < Doc.Blocks.Count && Doc.Blocks[at + 1] is { Kind: BlockKind.Para } nb && nb.IsEmpty;
        if (!nextEmpty)
        {
            Doc.Blocks.Insert(at + 1, new Block(new BlockKind.Para()));
        }

        Doc.Fix();
        Sel = Sel.Caret(Doc.Clamp(new Pos(at + 1, 0)));
        Notify();
    }

    public void InsertRule()
    {
        if (Plain)
        {
            return;
        }

        Cp(EditKind.Other);
        var p = Sel.Focus;
        var np = Doc.Blocks[p.B].Kind.IsCell ? new Pos(Doc.TableRange(p.B)?.End ?? (p.B + 1), 0) : Doc.Split(p);
        Doc.Blocks.Insert(np.B, new Block(new BlockKind.Rule()));
        Doc.Fix();
        Sel = Sel.Caret(new Pos(np.B + 1, 0));
        Notify();
    }

    public void InsertTable(int rows, int cols)
    {
        if (Plain || rows < 1 || cols < 1)
        {
            return;
        }

        Cp(EditKind.Other);
        DeleteSelection();
        var p = Sel.Focus;
        if (Doc.Blocks[p.B].Kind.IsCell)
        {
            Notify();
            return;
        }

        int at;
        if (Doc.Blocks[p.B].IsEmpty && !Doc.Blocks[p.B].Kind.IsAtomic)
        {
            Doc.Blocks.RemoveAt(p.B);
            at = p.B;
        }
        else
        {
            at = Doc.Split(p).B;
        }

        var t = Doc.NewTableId;
        var k = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                Doc.Blocks.Insert(at + k++, new Block(new BlockKind.Cell(t, r, c, Header: r == 0)));
            }
        }

        if (at + k >= Doc.Blocks.Count || Doc.Blocks[at + k].Kind.IsAtomic)
        {
            Doc.Blocks.Insert(at + k, new Block(new BlockKind.Para()));
        }

        Doc.Fix();
        Sel = Sel.Caret(new Pos(at, 0));
        Notify();
    }

    public bool ApplyTable(TableOp op)
    {
        var ok = TableOpCore(op);
        if (ok)
        {
            Notify();
        }

        return ok;
    }

    public bool CellTab(bool back)
    {
        if (CellPos() is not { } cp)
        {
            return false;
        }

        var (r, row, col) = cp;
        var (rows, cols) = Doc.TableDims(r);
        var idx = (row * cols) + col;
        if (back)
        {
            if (idx > 0)
            {
                var i = r.Start + idx - 1;
                Select(new Pos(i, 0), new Pos(i, Doc.Blocks[i].Length));
            }

            Notify();
            return true;
        }

        if (idx + 1 >= rows * cols)
        {
            TableOpCore(TableOp.RowBelow);
            var f = Sel.Focus.B;
            var i = f - ((f - Doc.TableRange(f)!.Value.Start) % cols);
            Sel = Sel.Caret(new Pos(i, 0));
            Notify();
            return true;
        }

        var n = r.Start + idx + 1;
        Select(new Pos(n, 0), new Pos(n, Doc.Blocks[n].Length));
        Notify();
        return true;
    }

    // ------------------------------------------------------------ replacement

    public void ReplaceText(string text, bool whole)
    {
        ArgumentNullException.ThrowIfNull(text);
        Cp(EditKind.Other);
        var blocks = text.TrimEnd().Split('\n').Select(Block.Para).ToList();
        if (whole)
        {
            var end = Doc.Blocks.ToList().FindIndex(b => b.Kind is BlockKind.Quoted);
            if (end < 0)
            {
                end = Doc.Blocks.Count;
            }

            // keep a reply header line ("On ... wrote:") right before the quote
            if (end < Doc.Blocks.Count && end > 0 && Doc.Blocks[end - 1].Text.TrimEnd().EndsWith(':'))
            {
                end--;
            }

            var tail = Doc.Blocks.Skip(end).ToList();
            Doc.Blocks.Clear();
            foreach (var b in blocks)
            {
                Doc.Blocks.Add(b);
            }

            if (tail.Count > 0)
            {
                Doc.Blocks.Add(new Block(new BlockKind.Para()));
            }

            foreach (var b in tail)
            {
                Doc.Blocks.Add(b);
            }

            Doc.Fix();
            var last = blocks.Count - 1;
            Sel = Sel.Caret(new Pos(last, Doc.Blocks[last].Length));
            Notify();
            return;
        }

        DeleteSelection();
        var p = Doc.InsertBlocks(Sel.Focus, blocks);
        Sel = Sel.Caret(p);
        Notify();
    }

    public bool ResizeImage(int block, int width, int height, bool undo)
    {
        if (block < 0 || block >= Doc.Blocks.Count || Doc.Blocks[block].Kind is not BlockKind.Image img)
        {
            return false;
        }

        if (undo)
        {
            Cp(EditKind.Other);
        }
        else
        {
            _version++;
        }

        Doc.Blocks[block].Kind = new BlockKind.Image(img.Data with { Width = Math.Max(0, width), Height = Math.Max(0, height) });
        Notify();
        return true;
    }

    public void ReplaceDoc(Doc doc, bool undo)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (undo)
        {
            Cp(EditKind.Other);
        }
        else
        {
            _version++;
        }

        Doc = doc;
        Doc.Fix();
        Sel = new Sel(Doc.Clamp(Sel.Anchor), Doc.Clamp(Sel.Focus));
        Notify();
    }

    public void ReplaceRange(Pos a, Pos b, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Select(a, b);
        InsertText(text);
    }

    public void ToggleQuoted(int block)
    {
        if (block >= 0 && block < Doc.Blocks.Count && Doc.Blocks[block].Kind is BlockKind.Quoted q)
        {
            Doc.Blocks[block].Kind = q with { Open = !q.Open };
            _version++;
            Notify();
        }
    }

    public bool SetImageUploaded(string cid, string blobId, string mime, string name)
    {
        var any = false;
        foreach (var b in Doc.Blocks)
        {
            if (b.Kind is BlockKind.Image img && string.Equals(img.Data.Cid, cid, StringComparison.Ordinal))
            {
                b.Kind = new BlockKind.Image(img.Data with { BlobId = blobId, Mime = mime, Name = name });
                any = true;
            }
        }

        if (any)
        {
            _version++;
            Notify();
        }

        return any;
    }

    private static (Doc Doc, Sel Sel) Pop(List<(Doc Doc, Sel Sel)> stack)
    {
        var v = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return v;
    }

    private static bool IsWordRune(Rune r) => Rune.IsLetterOrDigit(r) || r.Value is '_' or '\'' or '-';

    private static bool IsWordCoreRune(Rune r) => Rune.IsLetterOrDigit(r) || r.Value == '_';

    private static bool AllRunes(string s, int from, int to, Func<Rune, bool> pred)
    {
        foreach (var r in s[from..to].EnumerateRunes())
        {
            if (!pred(r))
            {
                return false;
            }
        }

        return true;
    }

    private static Block Blank(bool header = false) => new(new BlockKind.Cell(0, 0, 0, header));

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Takes a history snapshot unless this edit merges into the previous one (same kind, typing or deleting, within 1200 ms).</summary>
    private void Cp(EditKind kind)
    {
        var now = Time.GetTimestamp();
        var merge = kind is EditKind.Typing or EditKind.Delete
            && _last is { } l && l.Kind == kind && Time.GetElapsedTime(l.At, now) < MergeWindow;
        if (!merge)
        {
            Snapshot();
        }

        _redo.Clear();
        _last = (kind, now);
        _version++;
    }

    private void Snapshot()
    {
        _undo.Add((Doc.Clone(), Sel));
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }
    }

    private void AfterChange()
    {
        _last = null;
        _stored = null;
        _version++;
        Sel = new Sel(Doc.Clamp(Sel.Anchor), Doc.Clamp(Sel.Focus));
    }

    private void Select(Pos anchor, Pos focus)
    {
        Sel = new Sel(Doc.Clamp(anchor), Doc.Clamp(focus));
        _stored = null;
        BreakGroup();
    }

    private Marks? TakeStored()
    {
        var s = _stored;
        _stored = null;
        return s;
    }

    private (int Start, int End) BlockRange() => (Sel.Start.B, Sel.End.B);

    /// <summary>Deletes the selection if any; true when something was deleted.</summary>
    private bool DeleteSelection()
    {
        if (Sel.IsCollapsed)
        {
            return false;
        }

        var p = Doc.Delete(Sel.Start, Sel.End);
        Sel = Sel.Caret(p);
        return true;
    }

    private void ForEachSelectedRange(Action<Block, int, int> f)
    {
        var (a, b) = (Sel.Start, Sel.End);
        for (var i = a.B; i <= b.B; i++)
        {
            var blk = Doc.Blocks[i];
            if (blk.Kind.IsAtomic)
            {
                continue;
            }

            f(blk, i == a.B ? a.O : 0, i == b.B ? b.O : blk.Length);
        }
    }

    /// <summary>Applies <paramref name="f"/> to the marks of the selection, or to the stored marks at a caret.</summary>
    private void MapSelectionMarks(Func<Marks, Marks> f)
    {
        if (Sel.IsCollapsed)
        {
            _stored = f(CurrentMarks());
            _version++;
            return;
        }

        Cp(EditKind.Other);
        ForEachSelectedRange((blk, s, e) => blk.MapMarks(s, e, f));
    }

    /// <summary>Markdown-style shortcuts applied as you type (TipTap's input rules): block rules at the start of a paragraph, inline rules elsewhere.</summary>
    private void InputRules(char c)
    {
        var p = Sel.Focus;
        var blk = Doc.Blocks[p.B];
        var t = blk.Text;
        var before = t[..p.O];
        if (c == ' ' && blk.Kind is BlockKind.Para && before.Length >= 1)
        {
            var prefix = before[..^1];
            BlockKind? kind = prefix switch
            {
                "#" => new BlockKind.Heading(1),
                "##" => new BlockKind.Heading(2),
                "###" => new BlockKind.Heading(3),
                "-" or "*" or "+" => new BlockKind.Bullet(),
                ">" => new BlockKind.Quote(),
                "```" => new BlockKind.CodeBlock(),
                _ when prefix.Length >= 2 && prefix[^1] == '.' && prefix[..^1].All(char.IsAsciiDigit) => new BlockKind.Ordered(),
                _ => null,
            };
            if (kind is not null)
            {
                Snapshot();
                blk.Cut(0, p.O);
                blk.Kind = kind;
                Sel = Sel.Caret(new Pos(p.B, 0));
                _last = null; // the conversion is its own undo step
                return;
            }
        }

        if (blk.Kind is BlockKind.CodeBlock)
        {
            return;
        }

        // inline rules: **bold**, __bold__, ~~strike~~, `code`, *italic*
        (string D, Mark M)[] rules = [("**", Mark.Bold), ("__", Mark.Bold), ("~~", Mark.Strike), ("`", Mark.Code), ("*", Mark.Italic)];
        foreach (var (d, mark) in rules)
        {
            if (!before.EndsWith(d, StringComparison.Ordinal) || d[^1] != c)
            {
                continue;
            }

            var close = before.Length - d.Length;
            var open = before[..close].LastIndexOf(d, StringComparison.Ordinal);
            if (open < 0)
            {
                continue;
            }

            var inner = before[(open + d.Length)..close];
            if (inner.Length == 0 || inner[0] == ' ' || inner[^1] == ' ')
            {
                continue;
            }

            if (string.Equals(d, "*", StringComparison.Ordinal) && (inner.Contains('*', StringComparison.Ordinal) || before[..open].EndsWith('*')))
            {
                continue;
            }

            if (string.Equals(d, "`", StringComparison.Ordinal) && inner.Contains('`', StringComparison.Ordinal))
            {
                continue;
            }

            Snapshot();
            blk.Cut(close, close + d.Length);
            blk.Cut(open, open + d.Length);
            var (s, e) = (open, close - d.Length);
            var applied = mark;
            blk.MapMarks(s, e, m => applied.With(m, on: true));
            Sel = Sel.Caret(new Pos(p.B, e));
            _stored = applied.With(blk.MarksAt(e), on: false);
            _last = null;
            return;
        }
    }

    private void EnterCore(bool shift)
    {
        Cp(EditKind.Other);
        DeleteSelection();
        var p = Sel.Focus;
        var blk = Doc.Blocks[p.B];
        var kind = blk.Kind;
        var empty = blk.IsEmpty;
        if (shift || kind is BlockKind.Cell)
        {
            var m = blk.MarksAt(p.O);
            Sel = Sel.Caret(Doc.InsertText(p, "\n", m));
            return;
        }

        if (kind is BlockKind.CodeBlock)
        {
            var t = blk.Text;

            // Enter on an empty last line leaves the code block
            if (p.O == t.Length && (t.EndsWith('\n') || t.Length == 0))
            {
                if (t.Length > 0)
                {
                    blk.Cut(t.Length - 1, t.Length);
                }

                if (t.Length == 0)
                {
                    blk.Kind = new BlockKind.Para();
                    return;
                }

                Doc.Blocks.Insert(p.B + 1, new Block(new BlockKind.Para()));
                Sel = Sel.Caret(new Pos(p.B + 1, 0));
                return;
            }

            Sel = Sel.Caret(Doc.InsertText(p, "\n", Marks.Plain));
            return;
        }

        if (empty && (kind.IsList || kind is BlockKind.Quote))
        {
            if (kind.IsList && blk.Indent > 0)
            {
                blk.Indent--;
            }
            else
            {
                blk.Kind = new BlockKind.Para();
                blk.Indent = 0;
            }

            return;
        }

        var np = Doc.Split(p);

        // carry the caret marks into the new block (minus links)
        var carried = Doc.Blocks[p.B].MarksAt(p.O) with { Link = null };
        if (!carried.IsPlain && Doc.Blocks[np.B].IsEmpty)
        {
            _stored = carried;
        }

        Sel = Sel.Caret(np);
    }

    private void BackspaceCore(bool word)
    {
        if (!Sel.IsCollapsed)
        {
            Cp(EditKind.Delete);
            DeleteSelection();
            return;
        }

        var p = Sel.Focus;
        var blk = Doc.Blocks[p.B];
        if (p.O > 0)
        {
            var t = blk.Text;
            Cp(EditKind.Delete);
            var a = TextOffsets.PrevChar(t, p.O);
            if (word)
            {
                while (a > 0 && t[a - 1] == ' ')
                {
                    a = TextOffsets.PrevChar(t, a);
                }

                while (a > 0)
                {
                    var q = TextOffsets.PrevChar(t, a);
                    if (AllRunes(t, q, a, IsWordCoreRune))
                    {
                        a = q;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            Sel = Sel.Caret(Doc.Delete(new Pos(p.B, a), p));
            return;
        }

        // at the start of a block
        var kind = blk.Kind;
        if (kind.IsAtomic)
        {
            Cp(EditKind.Other);
            Doc.Blocks.RemoveAt(p.B);
            Doc.Fix();
            Sel = Sel.Caret(Doc.Clamp(new Pos(p.B > 0 ? p.B - 1 : 0, int.MaxValue)));
            if (p.B < Doc.Blocks.Count && p.B > 0)
            {
                Sel = Sel.Caret(new Pos(p.B - 1, Doc.Blocks[p.B - 1].Length));
            }

            return;
        }

        if (kind.IsList || kind is BlockKind.Quote or BlockKind.Heading or BlockKind.CodeBlock)
        {
            Cp(EditKind.Other);
            if (kind.IsList && blk.Indent > 0)
            {
                blk.Indent--;
            }
            else
            {
                blk.Kind = new BlockKind.Para();
                blk.Indent = 0;
            }

            return;
        }

        if (kind.IsCell || p.B == 0)
        {
            return;
        }

        var prev = Doc.Blocks[p.B - 1];
        if (prev.Kind.IsCell)
        {
            // leave the cell alone; just remove an empty paragraph after a table
            if (Doc.Blocks[p.B].IsEmpty && p.B + 1 < Doc.Blocks.Count)
            {
                Cp(EditKind.Other);
                Doc.Blocks.RemoveAt(p.B);
            }

            Sel = Sel.Caret(new Pos(p.B - 1, Doc.Blocks[p.B - 1].Length));
            return;
        }

        var (prevAtomic, l) = (prev.Kind.IsAtomic, prev.Length);
        Cp(EditKind.Other);
        if (prevAtomic)
        {
            Doc.Blocks.RemoveAt(p.B - 1);
            Sel = Sel.Caret(new Pos(p.B - 1, 0));
            return;
        }

        // an empty paragraph merges "up"; otherwise the block joins the previous one
        Sel = Sel.Caret(Doc.Delete(new Pos(p.B - 1, l), new Pos(p.B, 0)));
    }

    private void DeleteForwardCore(bool word)
    {
        if (!Sel.IsCollapsed)
        {
            Cp(EditKind.Delete);
            DeleteSelection();
            return;
        }

        var p = Sel.Focus;
        var blk = Doc.Blocks[p.B];
        var t = blk.Text;
        if (blk.Kind.IsAtomic)
        {
            Cp(EditKind.Other);
            Doc.Blocks.RemoveAt(p.B);
            Doc.Fix();
            Sel = Sel.Caret(Doc.Clamp(new Pos(p.B, 0)));
            return;
        }

        if (p.O < t.Length)
        {
            Cp(EditKind.Delete);
            var e = TextOffsets.NextChar(t, p.O);
            if (word)
            {
                while (e < t.Length)
                {
                    var q = TextOffsets.NextChar(t, e);
                    if (AllRunes(t, e, q, IsWordCoreRune))
                    {
                        e = q;
                    }
                    else
                    {
                        break;
                    }
                }

                while (e < t.Length && t[e] == ' ')
                {
                    e++;
                }
            }

            Doc.Delete(p, new Pos(p.B, e));
            return;
        }

        if (p.B + 1 >= Doc.Blocks.Count || blk.Kind.IsCell)
        {
            return;
        }

        var next = Doc.Blocks[p.B + 1];
        if (next.Kind.IsCell)
        {
            return;
        }

        var nextAtomic = next.Kind.IsAtomic;
        Cp(EditKind.Other);
        if (nextAtomic)
        {
            Doc.Blocks.RemoveAt(p.B + 1);
            Doc.Fix();
            return;
        }

        Doc.Delete(p, new Pos(p.B + 1, 0));
    }

    private (BlockSpan Range, int Row, int Col)? CellPos()
    {
        var b = Sel.Focus.B;
        if (Doc.TableRange(b) is { } r && Doc.Blocks[b].Kind is BlockKind.Cell c)
        {
            return (r, c.Row, c.Col);
        }

        return null;
    }

    private void FocusCell(int start, int row, int col)
    {
        var r = Doc.TableRange(start) ?? new BlockSpan(start, start + 1);
        var (_, cols) = Doc.TableDims(r);
        var i = Math.Min(start + (row * cols) + col, r.End - 1);
        Sel = Sel.Caret(new Pos(i, Doc.Blocks[i].Length));
    }

    private bool TableOpCore(TableOp op)
    {
        if (CellPos() is not { } cp)
        {
            return false;
        }

        var (r, row, col) = cp;
        Cp(EditKind.Other);
        var grid = Doc.TableGrid(r).Select(x => x.ToList()).ToList();
        var cols = grid.Count > 0 ? grid[0].Count : 0;
        var headerRow = grid.Count > 0 && grid[0][0].Kind is BlockKind.Cell { Header: true };
        var (nr, nc) = (row, col);
        switch (op)
        {
            case TableOp.RowAbove:
                grid.Insert(row, [.. Enumerable.Range(0, cols).Select(_ => Blank())]);
                break;
            case TableOp.RowBelow:
                grid.Insert(row + 1, [.. Enumerable.Range(0, cols).Select(_ => Blank())]);
                nr++;
                break;
            case TableOp.ColLeft:
                for (var ri = 0; ri < grid.Count; ri++)
                {
                    grid[ri].Insert(col, Blank(ri == 0 && headerRow));
                }

                break;
            case TableOp.ColRight:
                for (var ri = 0; ri < grid.Count; ri++)
                {
                    grid[ri].Insert(col + 1, Blank(ri == 0 && headerRow));
                }

                nc++;
                break;
            case TableOp.DelRow:
                grid.RemoveAt(row);
                nr = Math.Min(nr, Math.Max(grid.Count - 1, 0));
                break;
            case TableOp.DelCol:
                foreach (var rw in grid)
                {
                    rw.RemoveAt(col);
                }

                nc = Math.Min(nc, Math.Max(cols - 2, 0));
                break;
            case TableOp.ToggleHeader:
                foreach (var c in grid[0])
                {
                    c.Kind = new BlockKind.Cell(0, 0, 0, Header: !headerRow);
                }

                break;
            case TableOp.DelTable:
                grid.Clear();
                break;
            default:
                return false;
        }

        if (grid.Count == 0 || grid[0].Count == 0)
        {
            var start = r.Start;
            for (var k = 0; k < r.Count; k++)
            {
                Doc.Blocks.RemoveAt(r.Start);
            }

            if (start >= Doc.Blocks.Count || Doc.Blocks[start].Kind.IsCell)
            {
                Doc.Blocks.Insert(Math.Min(start, Doc.Blocks.Count), new Block(new BlockKind.Para()));
            }

            Doc.Fix();
            Sel = Sel.Caret(Doc.Clamp(new Pos(start, 0)));
            return true;
        }

        var first = Doc.SetTable(r, grid);
        FocusCell(first, nr, nc);
        return true;
    }
}
