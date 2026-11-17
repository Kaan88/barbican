#if AUTOMATION
// Mirrors: spec/automation-protocol.md `ui bento|nordic dark|light [more|normal]` and `theme dark|light`: in memory only (ThemeController.Preview), nothing is saved.

using Barbican.Automation;
using Barbican.App.Theme;
using Barbican.Presentation.Theme;

namespace Barbican.App.Shell.Automation;

/// <summary>The <c>ui</c> command: the style, the theme and the contrast of the window, as the settings would set them.</summary>
public sealed class UiCommand(ThemeController theme) : IAutomationCommand
{
    public string Name => "ui";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var t = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nordic = theme.Applied?.Nordic ?? false;
        var dark = theme.Applied?.Dark ?? true;
        var more = theme.Applied?.MoreContrast ?? false;
        foreach (var word in t)
        {
            switch (word.ToLowerInvariant())
            {
                case "bento": nordic = false; break;
                case "nordic": nordic = true; break;
                case "dark": dark = true; break;
                case "light": dark = false; break;
                case "more": more = true; break;
                case "normal": more = false; break;
                default: return Task.FromResult("error unknown word " + word);
            }
        }

        theme.Preview(new ThemeVariant(dark, more, nordic));
        return Task.FromResult("ok");
    }
}
#endif
