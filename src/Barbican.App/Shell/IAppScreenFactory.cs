// Mirrors: M5 plan section 4.12 (the shell seam). M4's ShellPage hosts the Calendar screen with its own field (CalendarHost); contacts and files must not each edit ShellPage, so con-ui generalises that one place (step 1, sync point S0) to a host that shows the screen of the factory whose App is the rail's active app,
// building it on first visit. con-ui owns ShellPage.xaml and ShellPage.xaml.cs for the whole milestone; files-ui registers its factory and never edits them. Contract from the M5 plan; owner con-ui.

using Barbican.Presentation.Shell;

namespace Barbican.App.Shell;

/// <summary>The screen of one rail app other than Mail, built on first visit and then only hidden (the mail screen keeps its state while hidden).</summary>
public interface IAppScreenFactory
{
    /// <summary>The rail app this screen belongs to (Contacts, Files; Calendar keeps its own host until M10).</summary>
    AppId App { get; }

    /// <summary>Whether the rail entry is enabled for the signed-in logins (the capability is advertised).</summary>
    bool IsAvailable();

    /// <summary>Builds the whole screen (sidebar, list, detail) once.</summary>
    UIElement Create();

    /// <summary>Called each time the screen is shown (reload when stale).</summary>
    void OnShown();
}
