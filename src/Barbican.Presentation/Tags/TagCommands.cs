// Mirrors: spec/menus.md MENU-TAG (Show messages, Mark all as read, New tag, New tag inside, Rename, Colour, Delete tag, Manage tags); Rust ctxmenu.rs pick (the tag prompts) :830;
// Bulwark components/layout/sidebar.tsx (tag row menu), settings Tags section. The prompts are the in-window prompt dialog. Spec I-055..I-058. Owner actions (m2-close-a).

using Barbican.Core.Common;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Tags;

/// <summary>The commands of the tag menu.</summary>
public static class TagCommands
{
    private static readonly Result<Unit> Done = Result<Unit>.Ok(default);

    /// <summary>The ids <see cref="Create"/> makes a command for.</summary>
    public static IReadOnlyList<string> Ids { get; } =
    [
        CommandIds.ShowTagMessages,
        CommandIds.MarkTagRead,
        CommandIds.NewTag,
        CommandIds.NewTagInside,
        CommandIds.RenameTag,
        CommandIds.SetTagColour,
        CommandIds.DeleteTag,
    ];

    /// <summary>One command per id of <see cref="Ids"/>, in that order. Settings (Manage tags) arrive in M6, so that row stays disabled.</summary>
    public static IReadOnlyList<IAppCommand> Create(TagService tags, SidebarViewModel sidebar, MailActionService actions, IMailWriterSource writers, IActiveMail active, IDialogService? dialogs)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(active);

        async Task<string?> AskAsync(string title, string initial, string confirm, CancellationToken ct) =>
            dialogs is null ? null : await dialogs.ShowAsync(new PromptDialogRequest(title, "Name", initial, confirm), ct).ConfigureAwait(true);

        // a prompt answer becomes a result; cancelling is not an error
        Result<Unit> Saved<T>(Result<T> r) => r.IsOk ? Done : Result<Unit>.Fail(r.Error!);

        return
        [
            new ActionCommand(CommandIds.ShowTagMessages, c => !string.IsNullOrEmpty(c.Argument), async (c, _) =>
            {
                await sidebar.SelectTagAsync(c.Argument!).ConfigureAwait(true);
                return Done;
            }),
            new ActionCommand(CommandIds.MarkTagRead, c => !string.IsNullOrEmpty(c.Argument) && active.AccountKey is { } key && writers.CanWrite(key), (c, ct) => actions.MarkTagReadAsync(c.Argument!, ct)),
            new ActionCommand(CommandIds.NewTag, _ => true, async (_, ct) =>
                await AskAsync("New tag", string.Empty, "Create", ct).ConfigureAwait(true) is { } name ? Saved(tags.Add(null, name)) : Done),
            new ActionCommand(CommandIds.NewTagInside, c => !string.IsNullOrEmpty(c.Argument), async (c, ct) =>
                await AskAsync("New tag inside " + tags.LabelOf(c.Argument!), string.Empty, "Create", ct).ConfigureAwait(true) is { } name ? Saved(tags.Add(c.Argument, name)) : Done),
            new ActionCommand(CommandIds.RenameTag, c => !string.IsNullOrEmpty(c.Argument), async (c, ct) =>
                await AskAsync("Rename tag", tags.LabelOf(c.Argument!), "Rename", ct).ConfigureAwait(true) is { } name ? tags.Rename(c.Argument!, name) : Done),
            new ActionCommand(CommandIds.SetTagColour, c => c.Argument is { } a && a.Contains('|', StringComparison.Ordinal), (c, _) =>
            {
                var parts = c.Argument!.Split('|', 2);
                return Task.FromResult(tags.SetColour(parts[0], parts[1]));
            }),
            new ActionCommand(CommandIds.DeleteTag, c => !string.IsNullOrEmpty(c.Argument), async (c, ct) =>
            {
                // the confirmation was the menu's own; a tag that is open as the list filter leaves it first
                if (string.Equals(sidebar.SelectedTagId, c.Argument, StringComparison.Ordinal))
                {
                    await sidebar.ClearTagAsync().ConfigureAwait(true);
                }

                return tags.Delete(c.Argument!);
            }),
        ];
    }
}
