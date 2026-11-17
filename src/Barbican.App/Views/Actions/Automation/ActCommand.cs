#if AUTOMATION
// Mirrors: spec/automation-protocol.md `act COMMAND [ARGUMENT]`: runs a registered IAppCommand through the registry exactly as a menu item or the selection bar does (CommandSurface.Automation), on the
// list's selection. The keys (Delete, z) belong to the key tables of stream menus; this command is how the scenarios run the actions until those tables call them.

using Barbican.Automation;
using Barbican.Presentation.Commands;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>act</c> command.</summary>
public sealed class ActCommand(ICommandRegistry commands, Barbican.Presentation.Mail.IActiveMail active) : IAutomationCommand
{
    public string Name => "act";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var text = (args ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "error usage: act COMMAND [ARGUMENT]";
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var id = space < 0 ? text : text[..space];
        var argument = space < 0 ? null : text[(space + 1)..].Trim();
        if (!commands.TryGet(id, out var command))
        {
            return "error unknown command " + id;
        }

        var context = new CommandContext(CommandSurface.Automation, [], null, argument, active.AccountKey);
        if (!command.CanExecute(context))
        {
            return "disabled " + id;
        }

        var result = await command.ExecuteAsync(context, cancellationToken);
        return result.IsOk ? "ok " + id : "error " + result.Error!.Message;
    }
}
#endif
