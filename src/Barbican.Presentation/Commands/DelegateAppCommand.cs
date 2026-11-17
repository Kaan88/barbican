// Mirrors: M2 plan section 4.1 (IAppCommand); Bulwark lib/command-registry.ts:53-96 (a command is an id, an enable rule and an action). Owner menus.

using Barbican.Core.Common;

namespace Barbican.Presentation.Commands;

/// <summary>A command that is a function: the shell commands of the menus stream.</summary>
public sealed class DelegateAppCommand(string id, Func<CommandContext, bool> canExecute, Func<CommandContext, CancellationToken, Task<Result<Unit>>> execute) : IAppCommand
{
    /// <inheritdoc />
    public string Id { get; } = id;

    /// <inheritdoc />
    public bool CanExecute(CommandContext context) => canExecute(context);

    /// <inheritdoc />
    public Task<Result<Unit>> ExecuteAsync(CommandContext context, CancellationToken ct) => execute(context, ct);

    /// <summary>A command that runs synchronously and always succeeds.</summary>
    public static DelegateAppCommand Sync(string id, Action<CommandContext> run, Func<CommandContext, bool>? canExecute = null) =>
        new(id, canExecute ?? (_ => true), (c, _) =>
        {
            run(c);
            return Task.FromResult(Result<Unit>.Ok(default));
        });
}
