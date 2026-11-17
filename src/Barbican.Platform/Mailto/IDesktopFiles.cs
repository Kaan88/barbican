// Mirrors: Rust mailto.rs write_registration (non-Windows branch :262): barbican.desktop under XDG_DATA_HOME/applications, update-desktop-database, xdg-mime default. Owner composer-ui.

namespace Barbican.Platform.Mailto;

/// <summary>The file and process doors of the Linux registration, so a test never writes into the home folder or runs xdg-mime.</summary>
public interface IDesktopFiles
{
    /// <summary>The applications folder (<c>$XDG_DATA_HOME/applications</c>, else <c>~/.local/share/applications</c>).</summary>
    string ApplicationsDir { get; }

    void WriteFile(string path, string content);

    void DeleteFile(string path);

    /// <summary>Runs a program; true when it exited with 0.</summary>
    bool Run(string program, IReadOnlyList<string> args);
}
