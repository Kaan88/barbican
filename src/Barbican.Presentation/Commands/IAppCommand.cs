// Mirrors: M2 plan section 4.1; Bulwark lib/command-registry.ts:53-96, lib/app-commands.ts, components/email/message-actions.ts; Rust ctxmenu.rs actions, palette.rs:92. Contract from stage F of M2; owner menus.

using Barbican.Core.Common;

namespace Barbican.Presentation.Commands;

/// <summary>One command (several streams register one each): menus, palette, keys, toolbars and the hover strip all run commands by id.</summary>
public interface IAppCommand
{
    /// <summary>A <see cref="CommandIds"/> constant.</summary>
    string Id { get; }

    /// <summary>Evaluated when a menu opens or a key arrives, never cached (no stale enable state).</summary>
    bool CanExecute(CommandContext context);

    /// <summary>Runs the command; an expected failure is a failed result the surface shows as a toast.</summary>
    Task<Result<Unit>> ExecuteAsync(CommandContext context, CancellationToken ct);
}
