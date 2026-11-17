// Mirrors: Rust attachments.rs:62 resize_to_fill(THUMB, THUMB, Triangle): scale to fill a square, cropping the overflow. Owner mailcore.

namespace Barbican.Core.Mail.Attachments;

/// <summary>Scales an image to a square thumbnail.</summary>
public static class ThumbnailScaler
{
    /// <summary>Centre-crops to a square and averages the source pixels under each target pixel (area average).</summary>
    public static ThumbnailImage Fill(ThumbnailImage source, int edge)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(edge, 1);
        var side = Math.Min(source.Width, source.Height);
        var left = (source.Width - side) / 2;
        var top = (source.Height - side) / 2;
        var output = new byte[edge * edge * 4];
        for (var dy = 0; dy < edge; dy++)
        {
            var y0 = top + (dy * side / edge);
            var y1 = Math.Max(y0 + 1, top + ((dy + 1) * side / edge));
            for (var dx = 0; dx < edge; dx++)
            {
                var x0 = left + (dx * side / edge);
                var x1 = Math.Max(x0 + 1, left + ((dx + 1) * side / edge));
                long r = 0, g = 0, b = 0, a = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var i = ((y * source.Width) + x) * 4;
                        r += source.Rgba[i];
                        g += source.Rgba[i + 1];
                        b += source.Rgba[i + 2];
                        a += source.Rgba[i + 3];
                        count++;
                    }
                }

                var o = ((dy * edge) + dx) * 4;
                output[o] = (byte)(r / count);
                output[o + 1] = (byte)(g / count);
                output[o + 2] = (byte)(b / count);
                output[o + 3] = (byte)(a / count);
            }
        }

        return new ThumbnailImage(edge, edge, output);
    }
}
