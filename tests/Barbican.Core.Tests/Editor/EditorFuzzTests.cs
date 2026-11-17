#pragma warning disable MA0002, MA0006, MA0051, CA5394 // seeded System.Random on purpose (reproducible sequences); the step table and the invariant check read top to bottom
using System.Text;
using Barbican.Core.Editor;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Editor;

/// <summary>
/// Seeded random command sequences over a small document: <see cref="Doc.Fix"/>'s invariants (an editable last block, no runs in atomic blocks, cells consecutive and addressable, runs normalised, a valid selection
/// that never splits a surrogate pair) hold after every command, and undoing everything restores the original document.
/// </summary>
public sealed class EditorFuzzTests
{
    private static readonly string[] Pool =
        ["a", "b", " ", "é", "\U0001F600", "é", "\n", "# ", "- ", "> ", "**", "*", "`", "1. ", "hello world", "```", "x_y", "~~", "z"];

    private static readonly Mark[] Marks5 = [Mark.Bold, Mark.Italic, Mark.Underline, Mark.Strike, Mark.Code];

    private static readonly BlockKind[] Kinds =
        [new BlockKind.Para(), new BlockKind.Heading(1), new BlockKind.Heading(2), new BlockKind.Quote(), new BlockKind.CodeBlock(), new BlockKind.Bullet(), new BlockKind.Ordered()];

    private static Doc Seed()
    {
        var d = new Doc(
        [
            Block.Para("Hello world"),
            Block.WithText(new BlockKind.Bullet(), "item \U0001F600 one", Marks.Plain with { Bold = true }),
            Block.WithText(new BlockKind.Bullet(), "item two", Marks.Plain),
            Block.Para("last énd"),
        ]);
        d.Fix();
        return d;
    }

    [Fact]
    public void Two_thousand_random_sequences_keep_the_invariants_and_undo_restores_the_original()
    {
        var undone = 0;
        var tablesSeen = 0;
        for (var seed = 0; seed < 2000; seed++)
        {
            var rng = new Random(seed);
            var e = new EditorSession(Seed(), new FakeTimeProvider());
            var original = Dump(e.Doc);
            var log = new List<string>();
            var steps = rng.Next(5, 30);
            for (var i = 0; i < steps; i++)
            {
                var what = Step(e, rng);
                log.Add(what);
                var problem = Check(e);
                Assert.True(problem is null, $"seed {seed}, step {i} ({what}): {problem}\nlog: {string.Join(" | ", log)}");
            }

            tablesSeen += e.Doc.Blocks.Any(b => b.Kind.IsCell) ? 1 : 0;
            while (e.CanUndo)
            {
                undone++;
                Assert.True(e.Undo());
                var problem = Check(e);
                Assert.True(problem is null, $"seed {seed}, undo: {problem}\nlog: {string.Join(" | ", log)}");
            }

            Assert.True(string.Equals(original, Dump(e.Doc), StringComparison.Ordinal), $"seed {seed}: undo did not restore the document\nlog: {string.Join(" | ", log)}\nwant: {original}\ngot:  {Dump(e.Doc)}");
            while (e.CanRedo)
            {
                e.Redo();
                Assert.True(Check(e) is null, $"seed {seed}, redo: {Check(e)}");
            }
        }

        Assert.True(undone > 10_000, $"the sequences barely edited anything ({undone} undo steps)");
        Assert.True(tablesSeen > 50, $"tables were rarely created ({tablesSeen})");
    }

    private static string Step(EditorSession e, Random rng)
    {
        var kind = rng.Next(22);
        switch (kind)
        {
            case 0 or 1 or 2:
                var t = Pool[rng.Next(Pool.Length)];
                e.InsertText(t);
                return $"insert {Escape(t)}";
            case 3:
                e.Enter(rng.Next(2) == 0);
                return "enter";
            case 4:
                var w = rng.Next(2) == 0;
                e.Backspace(w);
                return $"backspace {w}";
            case 5:
                var w2 = rng.Next(2) == 0;
                e.DeleteForward(w2);
                return $"delete {w2}";
            case 6:
                var m = Marks5[rng.Next(Marks5.Length)];
                e.ToggleMark(m);
                return $"toggle {m}";
            case 7:
                e.SetColor(rng.Next(2) == 0 ? "#112233" : null);
                e.SetSize(rng.Next(2) == 0 ? 20 : null);
                return "colour/size";
            case 8:
                var k = Kinds[rng.Next(Kinds.Length)];
                e.SetBlock(k);
                return $"block {k}";
            case 9:
                e.SetAlign((Align)rng.Next(4));
                return "align";
            case 10:
                var d = rng.Next(2) == 0 ? 1 : -1;
                e.Indent(d);
                return $"indent {d}";
            case 11:
                e.ClearFormat();
                return "clear";
            case 12:
                if (rng.Next(2) == 0)
                {
                    e.SetLink("x.example", "t");
                }
                else
                {
                    e.SetLink("", "");
                }

                return "link";
            case 13:
                switch (rng.Next(4))
                {
                    case 0:
                        e.InsertImage(new ImageData { Src = "https://x.example/i.png" });
                        return "image";
                    case 1:
                        e.InsertRule();
                        return "rule";
                    case 2 when e.Doc.Blocks.Count(b => b.Kind.IsCell) < 12:
                        e.InsertTable(rng.Next(1, 3), rng.Next(1, 3));
                        return "table";
                    default:
                        e.CellTab(rng.Next(2) == 0);
                        return "tab";
                }

            case 14:
                var op = (TableOp)rng.Next(8);
                e.ApplyTable(op);
                return $"table {op}";
            case 15:
                e.SetCaret(RandomPos(e, rng));
                return "caret";
            case 16 or 17:
                e.SelectRange(RandomPos(e, rng), RandomPos(e, rng));
                return "select";
            case 18:
                switch (rng.Next(3))
                {
                    case 0:
                        e.SelectAll();
                        return "select all";
                    case 1:
                        e.SelectWord(RandomPos(e, rng));
                        return "select word";
                    default:
                        e.SelectBlock(rng.Next(e.Doc.Blocks.Count));
                        return "select block";
                }

            case 19:
                e.ReplaceText(rng.Next(2) == 0 ? "r1\nr2" : "solo", whole: false);
                return "replace text";
            case 20:
                e.InsertBlocks(rng.Next(2) == 0 ? [Block.Para("P")] : [Block.Para("P1"), new Block(new BlockKind.Heading(1)) { Runs = { new Run("H", Marks.Plain) } }]);
                return "insert blocks";
            default:
                var p = RandomPos(e, rng);
                e.ReplaceRange(p, new Pos(p.B, e.Doc.Blocks[p.B].Length), "R");
                return "replace range";
        }
    }

    private static Pos RandomPos(EditorSession e, Random rng)
    {
        var b = rng.Next(e.Doc.Blocks.Count);
        return new Pos(b, rng.Next(e.Doc.Blocks[b].Length + 1)); // may fall inside a pair or cluster: the session clamps
    }

    private static string Escape(string s) => s.Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>The first broken invariant, or null.</summary>
    private static string? Check(EditorSession e)
    {
        var bl = e.Doc.Blocks;
        if (bl.Count == 0)
        {
            return "no blocks";
        }

        if (bl[^1].Kind.IsAtomic || bl[^1].Kind.IsCell)
        {
            return "last block is not editable: " + bl[^1].Kind;
        }

        var tables = new Dictionary<int, (int First, int Last, HashSet<(int, int)> Seen)>();
        for (var i = 0; i < bl.Count; i++)
        {
            var b = bl[i];
            if (b.Kind.IsAtomic && b.Runs.Count > 0)
            {
                return $"runs in atomic block {i}";
            }

            for (var r = 0; r < b.Runs.Count; r++)
            {
                if (b.Runs[r].Text.Length == 0)
                {
                    return $"empty run in block {i}";
                }

                if (r > 0 && b.Runs[r - 1].Marks == b.Runs[r].Marks)
                {
                    return $"unmerged runs in block {i}";
                }
            }

            if (b.Kind is BlockKind.Cell c)
            {
                if (tables.TryGetValue(c.TableId, out var t))
                {
                    if (t.Last != i - 1)
                    {
                        return $"table {c.TableId} is not consecutive at block {i}";
                    }

                    if (!t.Seen.Add((c.Row, c.Col)))
                    {
                        return $"table {c.TableId} has a duplicate cell ({c.Row},{c.Col})";
                    }

                    tables[c.TableId] = (t.First, i, t.Seen);
                }
                else
                {
                    tables[c.TableId] = (i, i, [(c.Row, c.Col)]);
                }
            }

            if (b.Indent < 0 || b.Indent > 6)
            {
                return $"indent {b.Indent} out of range in block {i}";
            }
        }

        foreach (var (id, (first, last, seen)) in tables)
        {
            var rows = seen.Max(x => x.Item1) + 1;
            var cols = seen.Max(x => x.Item2) + 1;
            if (rows * cols != last - first + 1)
            {
                return $"table {id} is not rectangular ({rows}x{cols} for {last - first + 1} cells)";
            }
        }

        foreach (var p in new[] { e.Sel.Anchor, e.Sel.Focus })
        {
            if (p.B < 0 || p.B >= bl.Count)
            {
                return $"selection block {p.B} out of range";
            }

            var t = bl[p.B].Text;
            if (p.O < 0 || p.O > t.Length)
            {
                return $"selection offset {p.O} out of range of {t.Length}";
            }

            if (p.O > 0 && p.O < t.Length && char.IsLowSurrogate(t[p.O]) && char.IsHighSurrogate(t[p.O - 1]))
            {
                return $"selection splits a surrogate pair at {p}";
            }
        }

        foreach (var b in bl)
        {
            if (HasLoneSurrogate(b.Text))
            {
                return "a lone surrogate in " + b.Kind;
            }
        }

        return null;
    }

    private static bool HasLoneSurrogate(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(s[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Dump(Doc d)
    {
        var sb = new StringBuilder();
        foreach (var b in d.Blocks)
        {
            sb.Append(b.Kind).Append('/').Append(b.Align).Append('/').Append(b.Indent).Append(':');
            foreach (var r in b.Runs)
            {
                sb.Append('[').Append(r.Text).Append('|').Append(r.Marks).Append(']');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
