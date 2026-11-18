// Mirrors: M2 plan section 3.3 (multi-registration of IAppCommand; ShellCommandSet). The calendar commands built once so each can be registered. Owner cal-ui.

using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>The calendar commands, built once.</summary>
public sealed class CalendarCommandSet(IReadOnlyList<IAppCommand> commands)
{
    /// <summary>How many commands <see cref="CalendarCommands.Create"/> returns (the registration adds one <see cref="IAppCommand"/> per index).</summary>
    public const int Count = 40;

    public IReadOnlyList<IAppCommand> Commands { get; } = commands;
}
