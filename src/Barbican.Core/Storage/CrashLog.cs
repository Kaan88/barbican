// Mirrors: nothing in Bulwark (a web page has no process to lose) or the Rust app (no panic hook at rust-final). M1 hotfix (sign-in): the
// HAND TEST 1 crash left only a Windows Error Report (CoreMessagingXP.dll, 0xc000027b, RPC_E_WRONG_THREAD) and no stack. The App installs the
// handlers (App/Composition/CrashGuard.cs); this writes one file per exception into the profile's logs folder.
// No secret is ever written on purpose: the file holds the exception's type, message, HRESULT and stack. Messages of this app never carry a
// password or a token (they carry at most an address the user typed); the logs stay on this computer.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Barbican.Core.Storage;

/// <summary>Writes unhandled exceptions to <c>&lt;data dir&gt;/logs/crash-*.log</c>, so the next crash leaves its stack behind.</summary>
public static class CrashLog
{
    /// <summary>The folder name under the profile's data dir.</summary>
    public const string FolderName = "logs";

    /// <summary>How many crash files are kept (the oldest go first).</summary>
    public const int Keep = 50;

    private static int _serial;

    /// <summary>The logs folder of <paramref name="paths"/>.</summary>
    public static string FolderOf(IProfilePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.DataDir, FolderName);
    }

    /// <summary>
    /// Writes <paramref name="error"/> (with <paramref name="source"/>: which handler saw it, and whether the process goes down) and returns
    /// the file, or null when nothing could be written. Never throws: it runs inside the last-chance handlers.
    /// </summary>
    public static string? Write(string folder, string source, Exception? error, bool fatal, TimeProvider time, string? note = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(folder);
            ArgumentNullException.ThrowIfNull(time);
            var now = time.GetUtcNow();
            Directory.CreateDirectory(folder);
            var n = Interlocked.Increment(ref _serial);
            var pid = Environment.ProcessId;
            var name = string.Create(CultureInfo.InvariantCulture, $"crash-{now:yyyyMMdd-HHmmss-fff}-{pid}-{n}.log");
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, Format(source, error, fatal, now, note), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Prune(folder);
            return path;
        }
#pragma warning disable CA1031 // the last-chance handler must not throw from its own logging (a full disk, a read-only profile)
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>The text of one crash file.</summary>
    public static string Format(string source, Exception? error, bool fatal, DateTimeOffset at, string? note = null)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"time: {at:O}\n");
        sb.Append(CultureInfo.InvariantCulture, $"source: {source}\n");
        sb.Append(CultureInfo.InvariantCulture, $"fatal: {(fatal ? "yes" : "no (the app went on)")}\n");
        sb.Append(CultureInfo.InvariantCulture, $"process: {Environment.ProcessId} thread: {Environment.CurrentManagedThreadId}\n");
        sb.Append(CultureInfo.InvariantCulture, $"version: {typeof(CrashLog).Assembly.GetName().Version}\n");
        if (note is { Length: > 0 })
        {
            sb.Append(CultureInfo.InvariantCulture, $"note: {note}\n");
        }

        for (var (e, depth) = (error, 0); e is not null && depth < 8; (e, depth) = (e.InnerException, depth + 1))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{(depth == 0 ? "exception" : "inner")}: {e.GetType().FullName} HRESULT 0x{e.HResult:X8}\n");
        }

        sb.Append('\n').Append(error?.ToString() ?? "(no exception object)").Append('\n');
        if (error is not null && error.StackTrace is null)
        {
            // an exception that was never thrown here (handed over by the framework) has no stack of its own: say where it was seen
            sb.Append("\nseen at:\n").Append(new StackTrace(1, fNeedFileInfo: true)).Append('\n');
        }

        return sb.ToString();
    }

    private static void Prune(string folder)
    {
        var files = new DirectoryInfo(folder).GetFiles("crash-*.log").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Keep);
        foreach (var f in files)
        {
            f.Delete();
        }
    }
}
