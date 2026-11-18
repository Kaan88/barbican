// Mirrors: M4 CalendarCommandSet (the commands built once so each can be registered). Owner con-ui.

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Contacts;

/// <summary>The contacts commands, built once.</summary>
public sealed class ContactsCommandSet(IReadOnlyList<IAppCommand> commands)
{
    public IReadOnlyList<IAppCommand> Commands { get; } = commands;
}
