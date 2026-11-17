// Mirrors: M2 plan section 4.1 (ICommandRegistry over the registered IAppCommand instances). Stage F of M2 writes it complete (it is a dictionary); owner menus.

using System.Diagnostics.CodeAnalysis;

namespace Barbican.Presentation.Commands;

/// <summary>The commands every stream registered, by id; a later registration of the same id wins (a stream replaces a stub).</summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, IAppCommand> _byId = new(StringComparer.Ordinal);
    private readonly List<IAppCommand> _all = [];

    public CommandRegistry(IEnumerable<IAppCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var command in commands)
        {
            if (_byId.TryGetValue(command.Id, out var previous))
            {
                _all.Remove(previous);
            }

            _byId[command.Id] = command;
            _all.Add(command);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IAppCommand> All => _all;

    /// <inheritdoc />
    public bool TryGet(string id, [NotNullWhen(true)] out IAppCommand? command) => _byId.TryGetValue(id, out command);
}
