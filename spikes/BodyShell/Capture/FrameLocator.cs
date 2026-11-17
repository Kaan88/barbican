using Windows.UI;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>
/// Finds the spike root in a captured image by its marker square (a window capture includes the
/// frame and title bar; an X11 root capture the whole screen), and the DIP scale from its size.
/// </summary>
internal static class FrameLocator
{
    public static (int X, int Y, double Scale)? Find(CapturedFrame f, Color marker, double markerDips)
    {
        for (var y = 0; y < f.Height; y++)
        {
            var row = y * f.Stride;
            for (var x = 0; x < f.Width; x++)
            {
                if (!Is(f, row + (x * 4), marker))
                {
                    continue;
                }

                var run = 0;
                while (x + run < f.Width && Is(f, row + ((x + run) * 4), marker))
                {
                    run++;
                }

                var down = 0;
                while (y + down < f.Height && Is(f, ((y + down) * f.Stride) + (x * 4), marker))
                {
                    down++;
                }

                if (run >= markerDips * 0.75 && down >= markerDips * 0.75)
                {
                    return (x, y, run / markerDips);
                }
            }
        }

        return null;
    }

    private static bool Is(CapturedFrame f, int i, Color c) =>
        Math.Abs(f.Bgra[i] - c.B) <= 3 && Math.Abs(f.Bgra[i + 1] - c.G) <= 3 && Math.Abs(f.Bgra[i + 2] - c.R) <= 3;
}
