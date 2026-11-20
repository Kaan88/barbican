#if AUTOMATION
// Mirrors: spec/automation-protocol.md `key CHORD`. Use the shell's focused shortcuts and XAML tab navigation in-process.

using Barbican.Automation;
using Barbican.Presentation.Input;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Shell.Automation;

/// <summary>The <c>key</c> command: replaces F1's "no key router yet" built-in.</summary>
public sealed class KeyCommand(IKeyRouter router) : IAutomationCommand
{
    internal static ShellPage? Shell { get; set; }

    public string Name => "key";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!KeyChordParser.TryParse(args, out var chord))
        {
            return Task.FromResult("error bad chord " + args);
        }

        if (Shell?.TryHandleFocusedKey(chord) == true || router.Handle(chord, KeySource.Automation))
        {
            return Task.FromResult("ok");
        }

        var tabbed = chord is { Key: "Tab", Control: false, Alt: false } && Shell is { } shell &&
            FocusManager.TryMoveFocus(chord.Shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next,
                new FindNextElementOptions { SearchRoot = shell });
        return Task.FromResult(tabbed ? "ok" : "unhandled");
    }
}
#endif
