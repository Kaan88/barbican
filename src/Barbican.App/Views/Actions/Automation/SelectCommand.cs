#if AUTOMATION
// Mirrors: spec/automation-protocol.md `select ROWID [shift|ctrl]`: the same list paths a click takes (Select, ToggleCheck, ExtendTo); no input is produced. ROWID is a message id or #N (0-based).

using Barbican.Automation;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>select</c> command.</summary>
public sealed class SelectCommand(MessageListViewModel list) : IAutomationCommand
{
    public string Name => "select";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var parts = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return Task.FromResult("error usage: select ROWID [shift|ctrl]");
        }

        var rows = list.Messages.ToList();
        var row = RowRef.Find(rows, parts[0]);
        if (row is null)
        {
            return Task.FromResult("error no such row " + parts[0]);
        }

        var mode = parts.Length > 1 ? parts[1] : string.Empty;
        switch (mode)
        {
            case "shift":
                list.ExtendTo(row);
                break;
            case "ctrl":
                list.ToggleCheck(row);
                break;
            default:
                list.Select(row, OpenReason.Click, ListDirection.None);
                break;
        }

        return Task.FromResult($"ok {row.Id} checked={list.CheckedCount}");
    }
}
#endif
