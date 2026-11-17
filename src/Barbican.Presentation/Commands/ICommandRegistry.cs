// Mirrors: M2 plan section 4.1 (ICommandRegistry); Bulwark lib/command-registry.ts:53-96. Contract from stage F of M2; owner menus.

using System.Diagnostics.CodeAnalysis;

namespace Barbican.Presentation.Commands;

/// <summary>The registered commands by id. A command id without a handler shows disabled in menus and the palette and is a no-op for keys.</summary>
public interface ICommandRegistry
{
    bool TryGet(string id, [NotNullWhen(true)] out IAppCommand? command);

    IReadOnlyList<IAppCommand> All { get; }
}
