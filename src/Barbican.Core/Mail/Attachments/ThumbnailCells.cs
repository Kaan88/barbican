// Mirrors: Rust attachments.rs:62 resize_to_fill(THUMB, THUMB, Triangle) through ThumbnailScaler.Fill (centre crop to a square, area
// average). The cells of Fill as running sums, so a decoder can add pixels row by row without holding the picture (Fable review 1 F-09).
// Owner mailcore.

namespace Barbican.Core.Mail.Attachments;

/// <summary>The output cells of a square thumbnail and their running sums.</summary>
public sealed class ThumbnailCells
{
    private readonly int _edge;
    private readonly int[] _x0;
    private readonly int[] _x1;
    private readonly int[] _y0;
    private readonly int[] _y1;
    private readonly long[] _sums;
    private readonly int[] _counts;

    /// <summary>The cells for a <paramref name="width"/> x <paramref name="height"/> source and a thumbnail of <paramref name="edge"/>.</summary>
    public ThumbnailCells(int width, int height, int edge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(edge, 1);
        _edge = edge;
        var side = Math.Min(width, height);
        var left = (width - side) / 2;
        var top = (height - side) / 2;
        (_x0, _x1) = Ranges(left, side, edge);
        (_y0, _y1) = Ranges(top, side, edge);
        _sums = new long[edge * edge * 4];
        _counts = new int[edge * edge];
    }

    /// <summary>True when source row <paramref name="y"/> lies under output row <paramref name="dy"/>.</summary>
    public bool RowIn(int dy, int y) => y >= _y0[dy] && y < _y1[dy];

    /// <summary>The first source column under output column <paramref name="dx"/>.</summary>
    public int X0(int dx) => _x0[dx];

    /// <summary>One past the last source column under output column <paramref name="dx"/>.</summary>
    public int X1(int dx) => _x1[dx];

    /// <summary>Adds one RGBA pixel to cell <paramref name="cell"/>.</summary>
    public void Add(int cell, ReadOnlySpan<byte> rgba)
    {
        var o = cell * 4;
        _sums[o] += rgba[0];
        _sums[o + 1] += rgba[1];
        _sums[o + 2] += rgba[2];
        _sums[o + 3] += rgba[3];
        _counts[cell]++;
    }

    /// <summary>The thumbnail: each cell's integer mean, as Fill computes it.</summary>
    public ThumbnailImage Image()
    {
        var output = new byte[_edge * _edge * 4];
        for (var cell = 0; cell < _counts.Length; cell++)
        {
            var count = Math.Max(1, _counts[cell]);
            for (var k = 0; k < 4; k++)
            {
                output[(cell * 4) + k] = (byte)(_sums[(cell * 4) + k] / count);
            }
        }

        return new ThumbnailImage(_edge, _edge, output);
    }

    // Fill: start = origin + d * side / edge, end = max(start + 1, origin + (d + 1) * side / edge)
    private static (int[] Start, int[] End) Ranges(int origin, int side, int edge)
    {
        var start = new int[edge];
        var end = new int[edge];
        for (var d = 0; d < edge; d++)
        {
            start[d] = origin + (int)((long)d * side / edge);
            end[d] = Math.Max(start[d] + 1, origin + (int)((long)(d + 1) * side / edge));
        }

        return (start, end);
    }
}
