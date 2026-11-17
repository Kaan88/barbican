// Mirrors: Bulwark lib/command-registry.ts (compose.*, mail.reply*), lib/keyboard-shortcuts (Ctrl+N and Ctrl+Shift+M and c compose, r / a / f answer, Ctrl+Shift+C and B show Cc and Bcc inside the composer); M3 plan section 6.4 step 2.
// The commands that open and steer the composer, registered by id so menus, the palette, the reader buttons and the keys run them. Owner composer-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Compose;

/// <summary>Creates the composer's commands.</summary>
public static class ComposerCommands
{
    /// <summary>How many commands <see cref="Create"/> returns.</summary>
    public const int Count = 8;

    public static IReadOnlyList<IAppCommand> Create(IComposerService composer, IActiveMail active, IAccountManager? accounts)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(active);
        string? Account(CommandContext c) => c.AccountKey ?? active.AccountKey ?? accounts?.Active?.Key;

        IAppCommand New(string id, ComposeKind kind) => new DelegateAppCommand(
            id,
            c => c.Targets.Count == 1 && Account(c) is not null,
            async (c, ct) =>
            {
                await composer.OpenAsync(new ComposeRequest(c.Targets[0].AccountKey, kind) { SourceEmailId = c.Targets[0].EmailId }, ct).ConfigureAwait(true);
                return Result<Unit>.Ok(default);
            });

        return
        [
            new DelegateAppCommand(
                CommandIds.Compose,
                c => Account(c) is not null,
                async (c, ct) =>
                {
                    await composer.OpenAsync(new ComposeRequest(Account(c)!, ComposeKind.New), ct).ConfigureAwait(true);
                    return Result<Unit>.Ok(default);
                }),
            New(CommandIds.Reply, ComposeKind.Reply),
            New(CommandIds.ReplyAll, ComposeKind.ReplyAll),
            New(CommandIds.Forward, ComposeKind.Forward),
            New(CommandIds.ForwardAsAttachment, ComposeKind.ForwardAsAttachment),
            new DelegateAppCommand(
                CommandIds.NewMessageTo,
                c => Account(c) is not null && !string.IsNullOrWhiteSpace(c.Argument),
                async (c, ct) =>
                {
                    await composer.OpenAsync(new ComposeRequest(Account(c)!, ComposeKind.New) { Prefill = new ComposePrefill { To = c.Argument!.Trim() } }, ct).ConfigureAwait(true);
                    return Result<Unit>.Ok(default);
                }),
            DelegateAppCommand.Sync(CommandIds.ComposeShowCc, _ => composer.Current?.RevealCc(), _ => composer.IsOpen),
            DelegateAppCommand.Sync(CommandIds.ComposeShowBcc, _ => composer.Current?.RevealBcc(), _ => composer.IsOpen),
        ];
    }
}
