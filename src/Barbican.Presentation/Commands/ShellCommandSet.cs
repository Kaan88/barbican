// Mirrors: M2 plan section 3.3 (multi-registration of IAppCommand). Owner menus.

namespace Barbican.Presentation.Commands;

/// <summary>The shell commands built once, so each can be registered as an <see cref="IAppCommand"/>.</summary>
public sealed class ShellCommandSet(IReadOnlyList<IAppCommand> commands)
{
    public IReadOnlyList<IAppCommand> Commands { get; } = commands;
}
