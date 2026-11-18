// Groups and merge: a group is a card of kind "group" whose members are keyed by uid (B-CON-005, B-CON-006); a merge patches the kept card, moves the group memberships, then deletes the others, in that order so a failure loses nothing (B-CON-024).
// Mirrors Rust contacts.rs:344 create_group, :351 set_member, contact_merge.rs:136 plan and :210 group_moves; Bulwark stores/contacts-store.ts (group members), lib/contacts/merge.ts; lib/plugin-hook-catalog.ts:199-200.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Contacts;

public sealed partial class ContactWriter
{
    public async Task<Result<string>> CreateGroupAsync(string accountKey, string? bookId, string name, IReadOnlyList<string> memberUids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(memberUids);
        if (name.Trim().Length == 0)
        {
            return Result<string>.Fail(new ValidationError("Give the group a name."));
        }

        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var target = await BookAsync(client.Value!, accountKey, bookId, ct).ConfigureAwait(false);
        if (!target.IsOk)
        {
            return Result<string>.Fail(target.Error!);
        }

        var members = new JsonObject();
        foreach (var uid in memberUids)
        {
            members[uid] = true;
        }

        var card = NewCard(new JsonObject { ["kind"] = "group", ["name"] = new JsonObject { ["full"] = name.Trim() }, ["members"] = members }, target.Value.BookId);
        var created = await ContactJmap.CreateAsync(client.Value!, "ContactCard", target.Value.Account, card, ct).ConfigureAwait(false);
        if (!created.IsOk)
        {
            return created;
        }

        var id = ContactIds.Ns(client.Value!.Session.AccountFor(ContactOptions.Capability), target.Value.Account, created.Value!);
        hooks.Fire(HookNames.OnContactGroupChange, () => new ContactGroupChangePayload(id, "created"));
        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return Result<string>.Ok(id);
    }

    public async Task<Result<Unit>> SetMembersAsync(string accountKey, string groupId, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(remove);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var patch = ContactPatches.Members(StoredCard(accountKey, groupId), add, remove);
        if (patch.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var done = await WritePatchAsync(client.Value!, accountKey, groupId, patch, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            hooks.Fire(HookNames.OnContactGroupMemberChange, () => new ContactGroupMembersPayload(groupId, [.. add], [.. remove]));
        }

        return done;
    }

    public async Task<Result<Unit>> MergeAsync(string accountKey, ContactMergePlan plan, IReadOnlyList<JsonNode> groups, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(groups);
        var keepId = plan.Keep.Str("id");
        if (keepId is null)
        {
            return Result<Unit>.Fail(new ValidationError("The card to keep has no id."));
        }

        // 1: what the kept card gains
        if (plan.Patch.Count > 0)
        {
            var patched = await UpdateAsync(accountKey, keepId, plan.Patch, ct).ConfigureAwait(false);
            if (!patched.IsOk)
            {
                return patched;
            }
        }

        // 2: the groups that listed a card that goes now list the kept one
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        foreach (var (groupId, members) in ContactMerge.GroupMoves(groups, plan.Keep, plan.Others))
        {
            var moved = await WritePatchAsync(client.Value!, accountKey, groupId, new JsonObject { ["members"] = members }, ct).ConfigureAwait(false);
            if (!moved.IsOk)
            {
                return moved;
            }

            hooks.Fire(HookNames.OnContactGroupChange, () => new ContactGroupChangePayload(groupId, "updated"));
        }

        // 3: the others
        var gone = plan.Others.Select(o => o.Str("id")).OfType<string>().ToList();
        return await DeleteAsync(accountKey, gone, ct).ConfigureAwait(false);
    }
}
