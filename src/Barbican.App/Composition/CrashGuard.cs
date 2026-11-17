// Mirrors: nothing in Bulwark or the Rust app. M1 hotfix (sign-in): HAND TEST 1 ended in a fail-fast (a stowed WinRT exception,
// RPC_E_WRONG_THREAD) with no log. The three last-chance handlers now write the stack to the profile's logs folder (Core/Storage/CrashLog.cs):
// - Application.UnhandledException: an exception that reached the UI thread's dispatcher (a posted action, an async void handler, a binding).
//   It is marked handled, so the app goes on instead of being killed; the log says so.
// - TaskScheduler.UnobservedTaskException: a faulted task nobody awaited (a fire-and-forget start-up task); marked observed.
// - AppDomain.UnhandledException: an exception on another thread; the process still ends, but the file is written first.

using Barbican.Core.Storage;

namespace Barbican.App.Composition;

/// <summary>Installs the crash log handlers once, as early as the application object exists.</summary>
public static class CrashGuard
{
    private static int _installed;

    /// <summary>The logs folder in use (null before <see cref="Install"/>).</summary>
    public static string? Folder { get; private set; }

    public static void Install(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        try
        {
            Folder = CrashLog.FolderOf(ProfilePaths.FromEnvironment());
        }
        catch (ArgumentException)
        {
            // a malformed BARBICAN_PROFILE: OnLaunched reports it; crashes still get a place
            Folder = Path.Combine(Path.GetTempPath(), "Barbican-crash-logs");
        }

        var folder = Folder;
        var time = TimeProvider.System;
        app.UnhandledException += (_, e) =>
        {
            // the framework's own message is kept: for a stowed WinRT exception it can say more than the managed object
            var file = CrashLog.Write(folder, "Application.UnhandledException", e.Exception, fatal: false, time, e.Message);
            Console.Error.WriteLine($"[barbican] unhandled on the UI thread (handled, logged to {file ?? "nowhere"}): {e.Exception}");
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _ = CrashLog.Write(folder, "TaskScheduler.UnobservedTaskException", e.Exception, fatal: false, time);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var file = CrashLog.Write(folder, "AppDomain.UnhandledException", e.ExceptionObject as Exception, e.IsTerminating, time, e.ExceptionObject is Exception ? null : e.ExceptionObject?.ToString());
            Console.Error.WriteLine($"[barbican] unhandled (logged to {file ?? "nowhere"}): {e.ExceptionObject}");
        };
    }
}
