using Windows.UI;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>A named reference colour; a pixel belongs to it when every channel is within the tolerance, or when <see cref="Test"/> says so.</summary>
internal sealed record ColorClass(string Name, Color Color, int Tolerance = 14)
{
    /// <summary>A custom test (b, g, r) instead of the colour distance, for families such as "magenta under any scrim".</summary>
    public Func<byte, byte, byte, bool>? Test { get; init; }

    public bool Matches(byte b, byte g, byte r) => Test is { } t
        ? t(b, g, r)
        : Math.Abs(r - Color.R) <= Tolerance && Math.Abs(g - Color.G) <= Tolerance && Math.Abs(b - Color.B) <= Tolerance;
}
