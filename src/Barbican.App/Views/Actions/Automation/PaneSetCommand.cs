#if AUTOMATION
// Mirrors: spec/automation-protocol.md `pane-set SIDE LIST`: the widths a drag of both handles would leave, through the same PanesViewModel (clamped, written to panes.json).

using System.Globalization;
using Barbican.Automation;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>pane-set</c> command.</summary>
public sealed class PaneSetCommand(PanesViewModel panes) : IAutomationCommand
{
    public string Name => "pane-set";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var parts = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !float.TryParse(parts[0], CultureInfo.InvariantCulture, out var side) || !float.TryParse(parts[1], CultureInfo.InvariantCulture, out var list))
        {
            return Task.FromResult("error usage: pane-set SIDE LIST");
        }

        panes.Resize("sidebar", side);
        panes.Commit("sidebar");
        panes.Resize("email-list", list);
        panes.Commit("email-list");
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"ok sidebar={panes.SidebarWidth} list={panes.ListWidth}"));
    }
}
#endif
