// Mirrors: M2 plan 4.1 (one IAppCommand per CommandIds constant; CanExecute is evaluated when a menu opens or a key arrives, never cached). Owner actions.

using Barbican.Core.Common;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Actions;

/// <summary>An <see cref="IAppCommand"/> made of two delegates; the action commands are all instances of it.</summary>
public sealed class ActionCommand(string id, Func<CommandContext, bool> canExecute, Func<CommandContext, CancellationToken, Task<Result<Unit>>> execute) : IAppCommand
{
    /// <inheritdoc />
    public string Id => id;

    /// <inheritdoc />
    public bool CanExecute(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return canExecute(context);
    }

    /// <inheritdoc />
    public Task<Result<Unit>> ExecuteAsync(CommandContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        return execute(context, ct);
    }
}
