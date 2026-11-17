namespace Barbican.Spike.BodyShell.Capture;

/// <summary>One captured frame: BGRA pixels, top-down, and its time on the <see cref="Clock"/> (100 ns units).</summary>
internal sealed record CapturedFrame(long Time, int Width, int Height, int Stride, byte[] Bgra);
