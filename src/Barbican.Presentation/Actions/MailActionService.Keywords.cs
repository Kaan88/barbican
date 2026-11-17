// Mirrors: Rust mail.rs resolve_toggle :965 (star and read flip from the first message), patch_local :887, ctxmenu.rs edit_tags :777; Bulwark lib/jmap/client.ts
// batchUpdateKeywords :3859, lib/email-action-toast.ts (KeywordRestorePoint: Undo puts back exactly the values the change touched), tri-state tags in
// components/email/tag-picker.tsx. Hooks onBeforeEmailReadStateChange / onEmailReadStateChange, ...StarToggle, ...KeywordChange. Owner actions.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    private const string Seen = "$seen";
    private const string Flagged = "$flagged";
    private const string Pinned = "$pinned";

    /// <summary>Marks messages read or unread (Rust "read!" / "unread!": never a toggle).</summary>
    public Task<Result<Unit>> SetReadAsync(IReadOnlyList<MailTarget> targets, bool read, CancellationToken ct) =>
        ChangeAsync(targets, new Dictionary<string, bool>(StringComparer.Ordinal) { [Seen] = read }, HookNames.OnBeforeEmailReadStateChange, HookNames.OnEmailReadStateChange,
            ids => new EmailReadPayload(ids, read), read ? "Marked as read" : "Marked as unread", ct);

    /// <summary>Marks one message read because it was opened: no undo entry, no toast (the reader's markAsReadDelay path).</summary>
    public Task<Result<Unit>> MarkOpenedReadAsync(MailTarget target, CancellationToken ct) =>
        ChangeAsync([target], new Dictionary<string, bool>(StringComparer.Ordinal) { [Seen] = true }, HookNames.OnBeforeEmailReadStateChange, HookNames.OnEmailReadStateChange,
            ids => new EmailReadPayload(ids, true), "Marked as read", ct, recordUndo: false);

    /// <summary>Read from the first message's state, then the same to all (Rust resolve_toggle :965).</summary>
    public Task<Result<Unit>> ToggleReadAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct) =>
        targets.Count == 0 ? Task.FromResult(Result<Unit>.Ok(default)) : SetReadAsync(targets, !targets[0].Keywords.Seen, ct);

    /// <summary>Stars or unstars messages.</summary>
    public Task<Result<Unit>> SetStarAsync(IReadOnlyList<MailTarget> targets, bool starred, CancellationToken ct) =>
        ChangeAsync(targets, new Dictionary<string, bool>(StringComparer.Ordinal) { [Flagged] = starred }, HookNames.OnBeforeEmailStarToggle, HookNames.OnEmailStarToggle,
            ids => new EmailStarPayload(ids, starred, null), starred ? "Starred" : "Unstarred", ct);

    /// <summary>Star from the first message's state, then the same to all.</summary>
    public Task<Result<Unit>> ToggleStarAsync(IReadOnlyList<MailTarget> targets, CancellationToken ct) =>
        targets.Count == 0 ? Task.FromResult(Result<Unit>.Ok(default)) : SetStarAsync(targets, !targets[0].Keywords.Flagged, ct);

    /// <summary>Pins or unpins messages (<c>$pinned</c>).</summary>
    public Task<Result<Unit>> SetPinnedAsync(IReadOnlyList<MailTarget> targets, bool pinned, CancellationToken ct) =>
        SetKeywordsAsync(targets, pinned ? [Pinned] : [], pinned ? [] : [Pinned], pinned ? "Pinned" : "Unpinned", ct);

    /// <summary>Adds or removes tags (<c>$label:&lt;id&gt;</c> keywords).</summary>
    public Task<Result<Unit>> SetTagAsync(IReadOnlyList<MailTarget> targets, string tagId, bool on, CancellationToken ct)
    {
        var keyword = TagKeyword(tagId);
        return SetKeywordsAsync(targets, on ? [keyword] : [], on ? [] : [keyword], on ? "Tag added" : "Tag removed", ct);
    }

    /// <summary>The keyword a tag is stored as on a message.</summary>
    public static string TagKeyword(string tagId)
    {
        ArgumentNullException.ThrowIfNull(tagId);
        return "$label:" + tagId;
    }

    /// <summary>
    /// The tri-state of a tag over a batch (tag picker): true when every message has it, null when some do, false when none (Bulwark batch-tag-button.tsx).
    /// </summary>
    public static bool? TagState(IReadOnlyList<MailTarget> targets, string tagId)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var keyword = TagKeyword(tagId);
        var with = targets.Count(t => t.Keywords.IsSet(keyword));
        return with == 0 ? false : with == targets.Count ? true : null;
    }

    /// <summary>Adds and removes keywords in one patch (tags, junk flags).</summary>
    public Task<Result<Unit>> SetKeywordsAsync(IReadOnlyList<MailTarget> targets, IReadOnlyList<string> added, IReadOnlyList<string> removed, string label, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removed);
        var patch = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var k in added)
        {
            patch[k] = true;
        }

        foreach (var k in removed)
        {
            patch[k] = false;
        }

        return ChangeAsync(targets, patch, HookNames.OnBeforeEmailKeywordChange, HookNames.OnEmailKeywordChange, ids => new EmailKeywordPayload(ids, added, removed), label, ct);
    }

    private Task<Result<Unit>> ChangeAsync<TPayload>(
        IReadOnlyList<MailTarget> targets,
        IReadOnlyDictionary<string, bool> patch,
        string interceptHook,
        string observerHook,
        Func<IReadOnlyList<string>, TPayload> payload,
        string label,
        CancellationToken ct,
        bool recordUndo = true)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return PerAccountAsync(targets, (account, group) => ChangeOneAccountAsync(account, group, patch, interceptHook, observerHook, payload, label, recordUndo, ct));
    }

    private async Task<Result<Unit>> ChangeOneAccountAsync<TPayload>(
        string account,
        IReadOnlyList<MailTarget> group,
        IReadOnlyDictionary<string, bool> patch,
        string interceptHook,
        string observerHook,
        Func<IReadOnlyList<string>, TPayload> payload,
        string label,
        bool recordUndo,
        CancellationToken ct)
    {
        // messages already in the wanted state are left out: nothing to write for them
        var changed = group.Where(t => patch.Any(kv => t.Keywords.IsSet(kv.Key) != kv.Value)).ToList();
        if (changed.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var writer = WriterFor(account);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var ids = Ids(changed);
        if (!await AllowAsync(interceptHook, () => payload(ids), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        var before = list.PatchRows(ids, e => WithKeywords(e, patch));
        var result = await writer.Value!.UpdateKeywordsAsync(ids, patch, ct).ConfigureAwait(true);
        var refusal = result.IsOk ? FirstRefusal(result.Value!) : null;
        if (!result.IsOk || refusal is not null)
        {
            list.PutRows(result.IsOk ? [.. before.Where(b => result.Value!.Refused.Any(r => string.Equals(r.Id, b.Id, StringComparison.Ordinal)))] : before);
            return Result<Unit>.Fail(Failed(result.Error ?? refusal!));
        }

        hooks.Fire(observerHook, () => payload(ids));
        if (recordUndo)
        {
            undo.Record(label, undoCt => UndoKeywordsAsync(account, changed, patch, undoCt));
        }

        return Result<Unit>.Ok(default);
    }

    /// <summary>Puts back exactly the keyword values the change touched, one write per distinct earlier state.</summary>
    private async Task<Result<Unit>> UndoKeywordsAsync(string account, IReadOnlyList<MailTarget> changed, IReadOnlyDictionary<string, bool> patch, CancellationToken ct)
    {
        var writer = WriterFor(account);
        if (!writer.IsOk)
        {
            return Result<Unit>.Fail(writer.Error!);
        }

        var groups = changed.GroupBy(t => string.Join('|', patch.Keys.Select(k => t.Keywords.IsSet(k))), StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var first = g.First();
            var back = patch.Keys.ToDictionary(k => k, k => first.Keywords.IsSet(k), StringComparer.Ordinal);
            var ids = Ids(g);
            list.PatchRows(ids, e => WithKeywords(e, back));
            var r = await writer.Value!.UpdateKeywordsAsync(ids, back, ct).ConfigureAwait(true);
            if (!r.IsOk || !r.Value!.AllAccepted)
            {
                await list.ReloadAsync().ConfigureAwait(true);
                return Result<Unit>.Fail(r.Error ?? FirstRefusal(r.Value!)!);
            }
        }

        return Result<Unit>.Ok(default);
    }
}
