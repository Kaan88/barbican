#if AUTOMATION
// Mirrors: spec/automation-protocol.md `ui bulwark|bento|nordic dark|light [more|normal]`: in memory only.

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
        var bulwark = theme.Applied?.Bulwark ?? true;
        var dark = theme.Applied?.Dark ?? true;
        var more = theme.Applied?.MoreContrast ?? false;
        foreach (var word in t)
        {
            switch (word.ToLowerInvariant())
            {
                case "bulwark": nordic = false; bulwark = true; break;
                case "bento": nordic = false; bulwark = false; break;
                case "nordic": nordic = true; bulwark = false; break;
                case "dark": dark = true; break;
                case "light": dark = false; break;
                case "more": more = true; break;
                case "normal": more = false; break;
                default: return Task.FromResult("error unknown word " + word);
            }
        }

        theme.Preview(new ThemeVariant(dark, more, nordic, bulwark));
        return Task.FromResult("ok");
    }
}
#endif
