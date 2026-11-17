#if AUTOMATION
// Mirrors: M2 hotfix (HAND TEST 2 "none of the menus on the side are accessible"): a pattern invoke can succeed where a real click is swallowed by
// the window's drag region, so this reads the non-client regions Windows really uses. `caption` gives the shell the chrome of a normal window
// (ShellPage.AttachWindow: content extended into the title bar, the title band set as the drag area; a hidden run skips it) and replies the
// Caption and Passthrough regions of InputNonClientPointerSource in logical px of the client area, so a script can check that no region
// covers the rail, the sidebar or anything else below the 38 px title band. The window stays off-screen and is never activated.

using System.Globalization;
using System.Text;
using Barbican.Automation;

namespace Barbican.App.Shell.Automation;

/// <summary>The <c>caption</c> command: the window's drag (caption) and passthrough regions after the normal-window chrome is attached.</summary>
public sealed class CaptionCommand : IAutomationCommand
{
    private static Window? _window;
    private static ShellPage? _shell;
    private static bool _attached;

    public string Name => "caption";

    /// <summary>Called by the App once the window and the shell exist (AUTOMATION builds only).</summary>
    public static void Use(Window window, ShellPage shell)
    {
        _window = window;
        _shell = shell;
    }

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
#if WINDOWS
        if (_window is not { } window || _shell is not { } shell)
        {
            return "error no window";
        }

        if (!_attached)
        {
            shell.AttachWindow(window);
            _attached = true;
            await Task.Delay(400, cancellationToken).ConfigureAwait(true);   // a layout pass, so the title band's rectangle is the drag area
        }

        var scale = shell.XamlRoot?.RasterizationScale ?? 1.0;
        if (window.AppWindow is not { } appWindow)
        {
            return "error no AppWindow";
        }

        var titleBar = appWindow.TitleBar;
        var source = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(appWindow.Id);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"scale={scale:0.##} extended={window.ExtendsContentIntoTitleBar} buttons-h={(titleBar?.Height ?? 0) / scale:0.#}");
        foreach (var kind in (Microsoft.UI.Input.NonClientRegionKind[])[Microsoft.UI.Input.NonClientRegionKind.Caption, Microsoft.UI.Input.NonClientRegionKind.Passthrough])
        {
            sb.Append(' ').Append(kind.ToString().ToLowerInvariant()).Append('=');
            var rects = source.GetRegionRects(kind) ?? [];
            sb.Append(rects.Length == 0 ? "-" : string.Join(";", rects.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.X / scale:0},{r.Y / scale:0},{r.Width / scale:0},{r.Height / scale:0}"))));
        }

        return sb.ToString();
#else
        await Task.CompletedTask.ConfigureAwait(true);
        return "n/a on Linux (the window manager draws the title bar)";
#endif
    }
}
#endif
