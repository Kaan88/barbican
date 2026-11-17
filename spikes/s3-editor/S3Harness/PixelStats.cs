using System.Globalization;
using System.Text;

namespace Barbican.Spikes.S3;

/// <summary>Ink measurements on a BGRA8 capture: rows with ink (text lines), ink extents, and a diff.</summary>
internal sealed class PixelStats
{
    public PixelStats(byte[] bgra, int width, int height)
    {
        Bgra = bgra;
        Width = width;
        Height = height;
    }

    public byte[] Bgra { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Darkness 0..255 of a pixel against the white background.</summary>
    public int Ink(int x, int y)
    {
        // Premultiplied BGRA composited over white: c + (255 - a).
        var i = ((y * Width) + x) * 4;
        var under = 255 - Bgra[i + 3];
        var lum = (((Bgra[i + 2] + under) * 299) + ((Bgra[i + 1] + under) * 587) + ((Bgra[i] + under) * 114)) / 1000;
        return 255 - Math.Min(lum, 255);
    }

    /// <summary>Text lines as (top, bottom, left, right) ink bands, in pixels.</summary>
    public List<(int Top, int Bottom, int Left, int Right)> Lines()
    {
        var lines = new List<(int, int, int, int)>();
        var top = -1;
        for (var y = 0; y <= Height; y++)
        {
            var has = y < Height && Enumerable.Range(0, Width).Any(x => Ink(x, y) > 40);
            if (has && top < 0)
            {
                top = y;
            }
            else if (!has && top >= 0)
            {
                var (l, r) = Extent(top, y);
                lines.Add((top, y - 1, l, r));
                top = -1;
            }
        }

        return lines;
    }

    public long TotalInk()
    {
        long sum = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                sum += Ink(x, y);
            }
        }

        return sum;
    }

    public static string Json(IEnumerable<(int Top, int Bottom, int Left, int Right)> lines)
    {
        var sb = new StringBuilder("[");
        sb.AppendJoin(",", lines.Select(l => string.Create(CultureInfo.InvariantCulture, $"[{l.Top},{l.Bottom},{l.Left},{l.Right}]")));
        return sb.Append(']').ToString();
    }

    private (int Left, int Right) Extent(int y0, int y1)
    {
        var (l, r) = (Width, -1);
        for (var y = y0; y < y1; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (Ink(x, y) > 40)
                {
                    l = Math.Min(l, x);
                    r = Math.Max(r, x);
                }
            }
        }

        return (l, r);
    }
}
