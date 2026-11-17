// Mirrors: spec/menus.md MENU-READER (Always move messages from..., from @domain, from this list, Always mark as read, Always tag with, Block sender, Manage rules) and the CommandIds of the
// quick rules (RuleMoveSender ... ManageRules); Rust reader.rs rx-* actions; Bulwark components/email/rules-menu.tsx. I-179.

using Barbican.Core.Common;
using Barbican.Core.Filters.QuickRules;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Rules;

/// <summary>The commands of the Rules menus.</summary>
public static class QuickRuleCommands
{
    /// <summary>What "Block sender" says when the account has no Junk folder.</summary>
    public const string NoJunkHint = "No Junk folder";

    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one per index).</summary>
    public const int Count = 7;

    /// <summary>The rule commands, one per <see cref="CommandIds"/> rule id.</summary>
    public static IReadOnlyList<IAppCommand> Create(QuickRuleService rules, Func<string, MenuTag?> tag)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tag);
        return
        [
            Folder(CommandIds.RuleMoveSender, rules, (f, _) => new QuickRulePreset.MoveSender(f)),
            Folder(CommandIds.RuleMoveDomain, rules, (f, s) => s.Domain is null ? null : new QuickRulePreset.MoveDomain(f)),
            Folder(CommandIds.RuleMoveList, rules, (f, s) => s.ListId is null ? null : new QuickRulePreset.MoveList(f)),
            Make(CommandIds.RuleMarkRead, _ => true, (c, ct) => rules.ApplyAsync(c.Targets[0], _ => new QuickRulePreset.MarkRead(), ct)),
            Make(
                CommandIds.RuleTag,
                c => c.Targets.Count > 0 && c.Argument is { Length: > 0 },
                (c, ct) => rules.ApplyAsync(c.Targets[0], _ => tag(c.Argument!) is { } t ? new QuickRulePreset.Tag(t.Id, t.Label) : null, ct)),
            Make(
                CommandIds.RuleBlockSender,
                _ => rules.JunkFolder() is not null,
                (c, ct) => rules.ApplyAsync(c.Targets[0], _ => rules.JunkFolder() is { } junk ? new QuickRulePreset.Block(junk) : null, ct)),
            DelegateAppCommand.Sync(CommandIds.ManageRules, _ => rules.Manage()),
        ];
    }

    private static DelegateAppCommand Folder(string id, QuickRuleService rules, Func<QuickRuleFolder, QuickRuleSubject, QuickRulePreset?> make) =>
        Make(
            id,
            c => c.Targets.Count > 0 && c.Argument is { Length: > 0 },
            (c, ct) => rules.ApplyAsync(c.Targets[0], s => rules.FolderOf(c.Argument!) is { } f ? make(f, s) : null, ct));

    private static DelegateAppCommand Make(string id, Func<CommandContext, bool> can, Func<CommandContext, CancellationToken, Task<Result<string>>> run) =>
        new(id, c => c.Targets.Count > 0 && can(c), async (c, ct) =>
        {
            var r = await run(c, ct).ConfigureAwait(false);
            return r.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(r.Error ?? new ValidationError("That did not work."));
        });
}
