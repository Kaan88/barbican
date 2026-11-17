// Mirrors: Rust mailto.rs write_registration :262 and unregister :310 (non-Windows). Writes the .desktop entry of Barbican.Core.Mailto.DefaultMailApp.DesktopEntry and asks xdg-mime to make it the mailto handler.
// Never run by a test with real doors and never by an agent. Owner composer-ui.

using Barbican.Core.Mailto;

namespace Barbican.Platform.Mailto;

/// <summary>Registers or removes Barbican as the mailto: handler of the desktop (Linux).</summary>
public sealed class DesktopEntryRegistrar(IDesktopFiles files) : IDefaultMailApp
{
    public const string FileName = "barbican.desktop";

    public string Register(string exe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exe);
        var dir = files.ApplicationsDir;
        files.WriteFile(Path.Combine(dir, FileName), DefaultMailApp.DesktopEntry(exe));
        _ = files.Run("update-desktop-database", [dir]);
        return files.Run("xdg-mime", ["default", FileName, "x-scheme-handler/mailto"])
            ? "Barbican now opens mailto: links."
            : throw new IOException("barbican.desktop was written, but xdg-mime could not set it as the default.");
    }

    public string Unregister()
    {
        var dir = files.ApplicationsDir;
        files.DeleteFile(Path.Combine(dir, FileName));
        _ = files.Run("update-desktop-database", [dir]);
        return "Barbican no longer handles mailto: links.";
    }
}
