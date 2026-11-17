#if AUTOMATION
// Mirrors: spec/automation-protocol.md `hover-sim ROW|off`: the VM state a pointer entering a row sets (the hover strip shows); real hover is a hand test.

using Barbican.Automation;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>hover-sim</c> command.</summary>
public sealed class HoverSimCommand(MessageListViewModel list) : IAutomationCommand
{
    public string Name => "hover-sim";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var arg = (args ?? string.Empty).Trim();
        if (arg.Length == 0)
        {
            return Task.FromResult("error usage: hover-sim ROW|off");
        }

        if (string.Equals(arg, "off", StringComparison.Ordinal))
        {
            list.SetHover(null);
            return Task.FromResult("ok off");
        }

        var row = RowRef.Find(list.Messages.ToList(), arg);
        if (row is null)
        {
            return Task.FromResult("error no such row " + arg);
        }

        list.SetHover(row.Id);
        return Task.FromResult("ok " + row.Id);
    }
}
#endif
