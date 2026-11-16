using System.Diagnostics;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>QPC time in 100 ns units: the base of Windows.Graphics.Capture's SystemRelativeTime, so frame times and app events share one clock.</summary>
internal static class Clock
{
    public static long Now => (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency));

    public static double Ms(long from, long to) => (to - from) / 10_000.0;
}
