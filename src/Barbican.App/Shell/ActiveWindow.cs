// Mirrors: M1.md section 5 (IBodyOverlayStrategy.Cover takes the window); the dialog host is the first caller. Owner menus (the shell sets it from ShellPage.AttachWindow).

namespace Barbican.App.Shell;

/// <summary>The application's main window, for the services that need it (the body overlay strategy).</summary>
public static class ActiveWindow
{
    public static Window? Current { get; private set; }

    public static void Set(Window window) => Current = window;
}
