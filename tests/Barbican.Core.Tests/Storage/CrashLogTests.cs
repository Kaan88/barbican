// M1 hotfix (sign-in): the crash log the App's last-chance handlers write (App/Composition/CrashGuard.cs). HAND TEST 1 left only a WER report.

using Barbican.Core.Storage;
using Barbican.Core.Tests.Settings.Prefs;

namespace Barbican.Core.Tests.Storage;

public sealed class CrashLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("outer", new IOException("wrong thread", unchecked((int)0x8001010E)));
        }
#pragma warning disable CA1031 // the test needs a thrown exception with a stack
        catch (Exception e)
#pragma warning restore CA1031
        {
            return e;
        }
    }

    [Fact]
    public void The_log_lands_in_the_profiles_logs_folder_with_the_stack_and_the_HRESULT()
    {
        var folder = CrashLog.FolderOf(new TestProfilePaths(_root));
        Assert.Equal(Path.Combine(_root, "data", "logs"), folder);
        var file = CrashLog.Write(folder, "Application.UnhandledException", Thrown(), fatal: false, TimeProvider.System, "framework message");
        Assert.NotNull(file);
        Assert.StartsWith("crash-", Path.GetFileName(file), StringComparison.Ordinal);
        var text = File.ReadAllText(file);
        Assert.Contains("source: Application.UnhandledException", text, StringComparison.Ordinal);
        Assert.Contains("fatal: no", text, StringComparison.Ordinal);
        Assert.Contains("HRESULT 0x8001010E", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), text, StringComparison.Ordinal);
        Assert.Contains("note: framework message", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_never_throws_and_old_logs_are_pruned()
    {
        // a folder that cannot be created (a file is in the way): nothing written, nothing thrown
        Directory.CreateDirectory(_root);
        var blocked = Path.Combine(_root, "file");
        File.WriteAllText(blocked, "x");
        Assert.Null(CrashLog.Write(Path.Combine(blocked, "logs"), "AppDomain.UnhandledException", null, fatal: true, TimeProvider.System));

        var folder = Path.Combine(_root, "logs");
        for (var i = 0; i < CrashLog.Keep + 5; i++)
        {
            Assert.NotNull(CrashLog.Write(folder, "TaskScheduler.UnobservedTaskException", Thrown(), fatal: false, TimeProvider.System));
        }

        Assert.Equal(CrashLog.Keep, Directory.GetFiles(folder, "crash-*.log").Length);
    }
}
