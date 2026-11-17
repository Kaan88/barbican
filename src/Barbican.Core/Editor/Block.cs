// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

using System.Text;

namespace Barbican.Core.Editor;

/// <summary>
/// One block of the document: its kind, alignment, list nesting and styled runs. Offsets are UTF-16
/// code units into <see cref="Text"/>. Mutable, like Rust's <c>Block</c>; <see cref="Clone"/> copies
/// the run list (runs themselves are immutable records).
/// </summary>
public sealed class Block
{
    private List<Run> _runs = [];

    public Block(BlockKind kind)
    {
        Kind = kind;
    }

    public BlockKind Kind { get; set; }

    public Align Align { get; set; }

    /// <summary>Nesting level for list items (0 = top level).</summary>
    public int Indent { get; set; }

    public IList<Run> Runs => _runs;

    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var r in Runs)
            {
                sb.Append(r.Text);
            }

            return sb.ToString();
        }
    }

    public int Length => _runs.Sum(r => r.Text.Length);

    public bool IsEmpty => Length == 0;

    public static Block Para(string text) => WithText(new BlockKind.Para(), text, Marks.Plain);

    public static Block WithText(BlockKind kind, string text, Marks marks)
    {
        ArgumentNullException.ThrowIfNull(text);
        var b = new Block(kind);
        if (text.Length > 0)
        {
            b.Runs.Add(new Run(text, marks));
        }

        return b;
    }

    public Block Clone() => new(Kind) { Align = Align, Indent = Indent, _runs = [.. _runs] };

    /// <summary>Merges neighbouring runs with equal marks and drops empty ones.</summary>
    public void Normalize()
    {
        var output = new List<Run>(_runs.Count);
        foreach (var r in Runs)
        {
            if (r.Text.Length == 0)
            {
                continue;
            }

            if (output.Count > 0 && output[^1].Marks == r.Marks)
            {
                output[^1] = output[^1] with { Text = output[^1].Text + r.Text };
            }
            else
            {
                output.Add(r);
            }
        }

        _runs = output;
    }

    public void Insert(int o, string text, Marks marks)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        var i = SplitAt(Math.Min(o, Length));
        _runs.Insert(i, new Run(text, marks));
        Normalize();
    }

    public void InsertRuns(int o, IEnumerable<Run> runs)
    {
        var i = SplitAt(Math.Min(o, Length));
        _runs.InsertRange(i, runs.ToList());
        Normalize();
    }

    /// <summary>Removes and returns the runs of the range [a, b).</summary>
    public IReadOnlyList<Run> Cut(int a, int b)
    {
        var len = Length;
        (a, b) = (Math.Min(a, len), Math.Min(b, len));
        if (a >= b)
        {
            return [];
        }

        var i = SplitAt(a);
        var j = SplitAt(b);
        var output = _runs.GetRange(i, j - i);
        _runs.RemoveRange(i, j - i);
        Normalize();
        return output;
    }

    public IReadOnlyList<Run> Slice(int a, int b) => Clone().Cut(a, b);

    /// <summary>Applies <paramref name="f"/> to the marks of the range [a, b).</summary>
    public void MapMarks(int a, int b, Func<Marks, Marks> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (a >= b)
        {
            return;
        }

        var i = SplitAt(a);
        var j = SplitAt(b);
        for (var k = i; k < j; k++)
        {
            Runs[k] = Runs[k] with { Marks = f(Runs[k].Marks) };
        }

        Normalize();
    }

    /// <summary>The marks in effect at a caret position: those of the character before it.</summary>
    public Marks MarksAt(int o)
    {
        var acc = 0;
        Marks? last = null;
        foreach (var r in Runs)
        {
            if (o > acc && o <= acc + r.Text.Length)
            {
                return r.Marks;
            }

            if (o == 0 && acc == 0)
            {
                last = r.Marks;
                break;
            }

            acc += r.Text.Length;
        }

        var m = last ?? Marks.Plain;

        // A link does not extend past its start.
        return o == 0 ? m with { Link = null } : m;
    }

    /// <summary>The runs overlapping [a, b), for "is the mark on everywhere" checks.</summary>
    public IReadOnlyList<Run> RunsIn(int a, int b)
    {
        var acc = 0;
        var output = new List<Run>();
        foreach (var r in Runs)
        {
            var (s, e) = (acc, acc + r.Text.Length);
            if (e > a && s < b)
            {
                output.Add(r);
            }

            acc = e;
        }

        return output;
    }

    /// <summary>Splits the runs so a run boundary falls at <paramref name="o"/>; returns the index of the run starting there.</summary>
    private int SplitAt(int o)
    {
        var acc = 0;
        for (var i = 0; i < Runs.Count; i++)
        {
            var r = Runs[i];
            var l = r.Text.Length;
            if (o == acc)
            {
                return i;
            }

            if (o < acc + l)
            {
                var cut = o - acc;
                Runs[i] = r with { Text = r.Text[..cut] };
                Runs.Insert(i + 1, r with { Text = r.Text[cut..] });
                return i + 1;
            }

            acc += l;
        }

        return Runs.Count;
    }
}
