using System.Diagnostics;

namespace Barbican.App.Composition;

/// <summary>Milliseconds since the OS started this process (the cold-start clock of the smoke run).</summary>
internal static class ProcessClock
{
    private static readonly DateTime Started = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public static double SinceStartMs() => (DateTime.UtcNow - Started).TotalMilliseconds;
}
