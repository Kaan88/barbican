// Mirrors: Rust mailto.rs desktop_dir :226 (XDG_DATA_HOME or ~/.local/share, then applications). The real doors of the Linux registration; constructed only when the user confirmed it. Owner composer-ui.

using System.Diagnostics;

namespace Barbican.Platform.Linux.Mailto;

/// <summary>The home folder and the processes the Linux registration uses.</summary>
public sealed class LinuxDesktopFiles : Barbican.Platform.Mailto.IDesktopFiles
{
    public string ApplicationsDir
    {
        get
        {
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var home = Environment.GetEnvironmentVariable("HOME");
            var baseDir = !string.IsNullOrEmpty(data) ? data : !string.IsNullOrEmpty(home) ? Path.Combine(home, ".local", "share") : throw new IOException("No home folder.");
            return Path.Combine(baseDir, "applications");
        }
    }

    public void WriteFile(string path, string content)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void DeleteFile(string path) => File.Delete(path);

    public bool Run(string program, IReadOnlyList<string> args)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false };
            foreach (var a in args)
            {
                start.ArgumentList.Add(a);
            }

            using var p = Process.Start(start);
            p?.WaitForExit(5000);
            return p is { HasExited: true, ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
