// Mirrors: PORT-PLAN.md §3.3 (no blocking IO on the UI thread) and M1-plan §6.3 ("a debug assertion when file IO runs on the UI thread");
// Rust settings.rs:136 moved prefs writes off the UI thread for the same reason. Core cannot see the UI dispatcher, so the App installs a
// probe at start-up (PlatformServices); without one (tests, tools) nothing is checked.

using System.Diagnostics;

namespace Barbican.Core.Storage;

/// <summary>Flags profile file IO that runs on the UI thread.</summary>
public static class IoThreadCheck
{
    private static Func<bool>? _isUiThread;
    private static int _violations;

    /// <summary>How many times profile IO ran on the UI thread since start (tests and the automation build read it).</summary>
    public static int Violations => Volatile.Read(ref _violations);

    /// <summary>Fail a Debug assertion on a violation (on by default; the tests of the counter turn it off, as a failed assertion ends the process).</summary>
    public static bool AssertOnViolation { get; set; } = true;

    /// <summary>Installs the UI-thread probe (the App's dispatcher); null removes it.</summary>
    public static void Install(Func<bool>? isUiThread) => Volatile.Write(ref _isUiThread, isUiThread);

    /// <summary>Counts a violation and, in Debug builds, fails an assertion when called on the UI thread.</summary>
    public static void AssertNotUiThread()
    {
        if (Volatile.Read(ref _isUiThread) is { } probe && probe())
        {
            Interlocked.Increment(ref _violations);
            if (AssertOnViolation)
            {
                Debug.Fail("Profile file IO on the UI thread: move it to the thread pool.");
            }
        }
    }
}
