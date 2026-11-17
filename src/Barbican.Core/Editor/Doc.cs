// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

using System.Text;

namespace Barbican.Core.Editor;

/// <summary>
/// The composer document: a flat list of blocks, each holding styled runs. Positions are
/// (block index, UTF-16 offset into the block text). Tables are consecutive <see cref="BlockKind.Cell"/>
/// blocks (row-major) sharing a table id, so every editable position is addressed the same way.
/// </summary>
public sealed class Doc
{
    private readonly List<Block> _blocks;

    public Doc()
        : this([new Block(new BlockKind.Para())])
    {
    }

    public Doc(IEnumerable<Block> blocks)
    {
        _blocks = [.. blocks];
    }

    public IList<Block> Blocks => _blocks;

    public Pos End => new(Blocks.Count - 1, Blocks[^1].Length);

    public bool IsEmpty => _blocks.TrueForAll(b => b.IsEmpty && b.Kind is BlockKind.Para);

    public int NewTableId => Blocks.Select(b => b.Kind.Table).Max() is int t ? t + 1 : 0;

    /// <summary>A deep copy (blocks are cloned, runs are immutable records): the undo history stores these.</summary>
    public Doc Clone() => new(_blocks.Select(b => b.Clone()));

    public static Doc FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var d = new Doc(text.Split('\n').Select(Block.Para));
        d.Fix();
        return d;
    }

    /// <summary>Keeps the invariants: at least one block, an editable block at the end, no runs in atomic blocks.</summary>
    public void Fix()
    {
        if (Blocks.Count == 0)
        {
            Blocks.Add(new Block(new BlockKind.Para()));
        }

        if (Blocks[^1].Kind.IsAtomic || Blocks[^1].Kind.IsCell)
        {
            Blocks.Add(new Block(new BlockKind.Para()));
        }

        foreach (var b in Blocks)
        {
            if (b.Kind.IsAtomic)
            {
                b.Runs.Clear();
            }

            b.Normalize();
        }
    }

    public Pos Clamp(Pos p)
    {
        var b = Math.Clamp(p.B, 0, Blocks.Count - 1);
        return new Pos(b, TextOffsets.Floor(Blocks[b].Text, p.O));
    }

    /// <summary>Plain text of a range, blocks joined with newlines; a quoted original becomes "&gt; " lines.</summary>
    public string TextRange(Pos a, Pos b)
    {
        var sb = new StringBuilder();
        for (var i = a.B; i <= Math.Min(b.B, Blocks.Count - 1); i++)
        {
            var blk = Blocks[i];
            var t = blk.Text;
            var s = i == a.B ? Math.Min(a.O, t.Length) : 0;
            var e = i == b.B ? Math.Min(b.O, t.Length) : t.Length;
            if (i > a.B)
            {
                sb.Append('\n');
            }

            if (blk.Kind is BlockKind.Quoted q)
            {
                sb.AppendJoin('\n', Lines(q.Text).Select(l => "> " + l));
            }
            else
            {
                sb.Append(t, s, Math.Max(e, s) - s);
            }
        }

        return sb.ToString();
    }

    /// <summary>The blocks of a range as a fragment, the first and last trimmed to the range.</summary>
    public IReadOnlyList<Block> Fragment(Pos a, Pos b)
    {
        var output = new List<Block>();
        for (var i = a.B; i <= Math.Min(b.B, Blocks.Count - 1); i++)
        {
            var blk = Blocks[i];
            var nb = blk.Clone();
            if (!blk.Kind.IsAtomic)
            {
                var s = i == a.B ? a.O : 0;
                var e = i == b.B ? b.O : blk.Length;
                nb.Runs.Clear();
                nb.InsertRuns(0, blk.Slice(s, e));
            }

            output.Add(nb);
        }

        return output;
    }

    /// <summary>Deletes the range between a and b; returns the caret position afterwards.</summary>
    public Pos Delete(Pos a, Pos b)
    {
        (a, b) = (Pos.Min(a, b), Pos.Max(a, b));
        if (a == b)
        {
            return a;
        }

        if (a.B == b.B)
        {
            if (!Blocks[a.B].Kind.IsAtomic)
            {
                Blocks[a.B].Cut(a.O, b.O);
            }

            return a;
        }

        var firstCell = Blocks[a.B].Kind.IsCell;
        var lastCell = Blocks[b.B].Kind.IsCell;

        var remove = ClearMiddle(a.B, b.B);

        var firstAtomic = Blocks[a.B].Kind.IsAtomic;
        if (!firstAtomic)
        {
            Blocks[a.B].Cut(a.O, Blocks[a.B].Length);
        }

        var lastAtomic = Blocks[b.B].Kind.IsAtomic;
        if (lastAtomic)
        {
            remove.Add(b.B);
        }
        else if (firstCell || lastCell || firstAtomic)
        {
            Blocks[b.B].Cut(0, b.O);
        }
        else
        {
            // Merge the tail of the last block into the first.
            var tail = Blocks[b.B].Cut(b.O, Blocks[b.B].Length);
            Blocks[a.B].InsertRuns(a.O, tail);
            remove.Add(b.B);
        }

        var caret = a;
        if (firstAtomic && a.O == 0)
        {
            remove.Add(a.B);
            caret = new Pos(a.B, 0);
        }

        foreach (var i in remove.Reverse())
        {
            Blocks.RemoveAt(i);
        }

        Fix();
        return Clamp(caret);
    }

    public Pos InsertText(Pos p, string text, Marks marks)
    {
        ArgumentNullException.ThrowIfNull(text);
        p = Clamp(p);
        if (Blocks[p.B].Kind.IsAtomic)
        {
            Blocks.Insert(p.B + 1, new Block(new BlockKind.Para()));
            return InsertText(new Pos(p.B + 1, 0), text, marks);
        }

        Blocks[p.B].Insert(p.O, text, marks);
        return p with { O = p.O + text.Length };
    }

    /// <summary>Splits the block at p (Enter); returns the caret at the start of the new block.</summary>
    public Pos Split(Pos p)
    {
        p = Clamp(p);
        var blk = Blocks[p.B];
        if (blk.Kind.IsAtomic)
        {
            Blocks.Insert(p.B + 1, new Block(new BlockKind.Para()));
            return new Pos(p.B + 1, 0);
        }

        var tail = blk.Cut(p.O, blk.Length);
        var kind = blk.Kind is BlockKind.Heading && tail.Count == 0 ? new BlockKind.Para() : blk.Kind;
        var nb = new Block(kind) { Align = blk.Align, Indent = blk.Indent };
        nb.InsertRuns(0, tail);
        Blocks.Insert(p.B + 1, nb);
        return new Pos(p.B + 1, 0);
    }

    /// <summary>Inserts whole blocks at p, splitting the block there (paste, templates, signatures).</summary>
    public Pos InsertBlocks(Pos p, IReadOnlyList<Block> fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        p = Clamp(p);
        if (fragment.Count == 0)
        {
            return p;
        }

        var frag = fragment.Select(b => b.Clone()).ToList();
        var here = Blocks[p.B];

        // A single text block pastes inline.
        if (frag.Count == 1 && IsText(frag[0]) && !here.Kind.IsAtomic)
        {
            var n = frag[0].Length;
            here.InsertRuns(p.O, frag[0].Runs);
            return p with { O = p.O + n };
        }

        if (here.Kind.IsCell)
        {
            // Paste into a cell as lines.
            var lines = string.Join('\n', frag.Select(b => b.Text));
            return InsertText(p, lines, here.MarksAt(p.O));
        }

        RenumberTables(frag);
        return InsertSplit(p, frag);
    }

    /// <summary>Ordinal number of an ordered list item, counting siblings at the same level.</summary>
    public int Ordinal(int i)
    {
        var lvl = Blocks[i].Indent;
        var n = 1;
        for (var j = i - 1; j >= 0; j--)
        {
            var b = Blocks[j];
            if (!b.Kind.IsList || b.Indent < lvl)
            {
                break;
            }

            if (b.Indent == lvl)
            {
                if (b.Kind is not BlockKind.Ordered)
                {
                    break;
                }

                n++;
            }
        }

        return n;
    }

    /// <summary>The span of block indices forming the table block <paramref name="i"/> belongs to.</summary>
    public BlockSpan? TableRange(int i)
    {
        if (i < 0 || i >= Blocks.Count || Blocks[i].Kind.Table is not int t)
        {
            return null;
        }

        var s = i;
        while (s > 0 && Blocks[s - 1].Kind.Table == t)
        {
            s--;
        }

        var e = i + 1;
        while (e < Blocks.Count && Blocks[e].Kind.Table == t)
        {
            e++;
        }

        return new BlockSpan(s, e);
    }

    public (int Rows, int Cols) TableDims(BlockSpan r)
    {
        var (rows, cols) = (0, 0);
        for (var i = r.Start; i < r.End; i++)
        {
            if (Blocks[i].Kind is BlockKind.Cell c)
            {
                rows = Math.Max(rows, c.Row + 1);
                cols = Math.Max(cols, c.Col + 1);
            }
        }

        return (rows, cols);
    }

    /// <summary>Rebuilds a table from a grid of cell blocks; returns the index of its first block.</summary>
    public int SetTable(BlockSpan r, IReadOnlyList<IReadOnlyList<Block>> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var t = Blocks[r.Start].Kind.Table ?? 0;
        var cells = new List<Block>();
        for (var ri = 0; ri < grid.Count; ri++)
        {
            for (var ci = 0; ci < grid[ri].Count; ci++)
            {
                var c = grid[ri][ci].Clone();
                var header = c.Kind is BlockKind.Cell { Header: true };
                c.Kind = new BlockKind.Cell(t, ri, ci, header);
                cells.Add(c);
            }
        }

        _blocks.RemoveRange(r.Start, r.Count);
        _blocks.InsertRange(r.Start, cells);
        if (cells.Count == 0)
        {
            Fix();
        }

        return r.Start;
    }

    public IReadOnlyList<IList<Block>> TableGrid(BlockSpan r)
    {
        var (rows, cols) = TableDims(r);
        var grid = Enumerable.Range(0, rows)
            .Select(IList<Block> (_) => Enumerable.Range(0, cols).Select(_ => new Block(new BlockKind.Cell(0, 0, 0, Header: false))).ToList())
            .ToList();
        for (var i = r.Start; i < r.End; i++)
        {
            if (Blocks[i].Kind is BlockKind.Cell c)
            {
                grid[c.Row][c.Col] = Blocks[i].Clone();
            }
        }

        return grid;
    }

    /// <summary>Blocks strictly between first and last: cells are emptied (tables keep their shape), the others are returned for removal.</summary>
    private SortedSet<int> ClearMiddle(int first, int last)
    {
        var remove = new SortedSet<int>();
        for (var i = first + 1; i < last; i++)
        {
            if (Blocks[i].Kind.IsCell)
            {
                Blocks[i].Runs.Clear();
            }
            else
            {
                remove.Add(i);
            }
        }

        return remove;
    }

    private static bool IsText(Block b) => !b.Kind.IsAtomic && !b.Kind.IsCell;

    /// <summary>Rust's <c>str::lines</c>: split on \n, strip a trailing \r, no empty last line.</summary>
    private static IEnumerable<string> Lines(string s)
    {
        var parts = s.Split('\n');
        var n = parts.Length > 0 && parts[^1].Length == 0 ? parts.Length - 1 : parts.Length;
        return parts.Take(n).Select(l => l.EndsWith('\r') ? l[..^1] : l);
    }

    private void RenumberTables(List<Block> frag)
    {
        var tablesBase = NewTableId;
        foreach (var b in frag)
        {
            if (b.Kind is BlockKind.Cell c)
            {
                b.Kind = c with { TableId = c.TableId + tablesBase };
            }
        }
    }

    /// <summary>The multi-block case of <see cref="InsertBlocks"/>: split at p, merge the edges, insert the rest.</summary>
    private Pos InsertSplit(Pos p, List<Block> frag)
    {
        var hereAtomic = Blocks[p.B].Kind.IsAtomic;
        var firstText = IsText(frag[0]);
        var atStartEmpty = Blocks[p.B].IsEmpty && !hereAtomic;
        var after = hereAtomic ? new Pos(p.B + 1, 0) : Split(p);
        if (!hereAtomic && firstText)
        {
            // The first fragment block merges into the block before the split.
            var first = frag[0];
            frag.RemoveAt(0);
            var here = Blocks[p.B];
            if (atStartEmpty)
            {
                here.Kind = first.Kind;
                here.Align = first.Align;
                here.Indent = first.Indent;
            }

            here.InsertRuns(here.Length, first.Runs);
        }

        var n = frag.Count;
        var lastIsText = n > 0 && IsText(frag[^1]);
        _blocks.InsertRange(after.B, frag);
        var caret = new Pos(after.B + n, 0);
        var tailIdx = after.B + n;
        if (lastIsText && tailIdx < Blocks.Count && IsText(Blocks[tailIdx]))
        {
            // Merge the split-off tail into the last inserted block.
            var li = tailIdx - 1;
            var o = Blocks[li].Length;
            Blocks[li].InsertRuns(o, Blocks[tailIdx].Runs);
            Blocks.RemoveAt(tailIdx);
            caret = new Pos(li, o);
        }

        Fix();
        return Clamp(caret);
    }
}
