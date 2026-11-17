// Mirrors: ShellCommandSet (the commands are created once and registered one IAppCommand each). Owner composer-ui.

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Compose;

/// <summary>The composer's commands, created once.</summary>
public sealed class ComposerCommandSet(IReadOnlyList<IAppCommand> commands)
{
    public IReadOnlyList<IAppCommand> Commands { get; } = commands;
}
