// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.batch.ts:20-146 doEmailBatch (email:write, c.ts:30; one object { emailIds, action, mailboxId?, tagId? }; at most 200 distinct ids; action archive | move | read | unread | tag | untag;
// mailboxId only for move and only one of the account's own folders that nobody else reads and that is not the trash; tagId only for tag and untag and one of the user's tags; the messages of the signed-in account only;
// 20 moves a minute shared with email.move; restore points taken first and one entry in the app's undo history; resolves { done, failed, undoToken }), lib/mailbox-shared-out.ts:13-42 (isSharedOut),
// lib/thread-utils.ts:245-247 ($label: and the legacy $color: tag keywords). Stalwart crates/jmap/src/email/set.rs. Moves run the same hooks as the list's own (onBeforeEmailMove / onAfterEmailMove,
// onBeforeEmailArchive / onEmailArchive); an intercept that says no is { done: 0, failed: n, undoToken: null }.
// Deviations: archive moves into the Archive folder itself, not the user's per-year or per-month folders; the administrator "archiving off" switch has no Barbican setting; messages the server refuses are counted as failed and
// the others still count (Bulwark answers all-or-nothing from its store); the app's list and reader change through the sync, not directly.
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Mail.Write;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailRules;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailBatch(PluginMailActionHost host) : PluginMailActionMethod("email.batch", "email:write", host, true, slow: true)
{
    internal const string UndoKind = "batch";
    private static readonly string[] Allowed = ["emailIds", "action", "mailboxId", "tagId"];
    private static readonly string[] Actions = ["archive", "move", "read", "unread", "tag", "untag"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryRead(PluginMailArgs.At(arguments, 0), out _) ? null : PluginErrorCode.InvalidArguments;

    private sealed record Batch(IReadOnlyList<string> Ids, string Action, string MailboxId, string TagId)
    {
        internal bool Moving => Is(Action, "archive") || Is(Action, "move");

        internal bool Tagging => Is(Action, "tag") || Is(Action, "untag");
    }

    private readonly record struct Applied(PluginCallOutcome? Early, int Count, string Label);

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static bool TryRead(PluginValue? value, out Batch batch)
    {
        batch = new([], "", "", "");
        if (!PluginMailArgs.TryOptions(value, Allowed, true, out var data) || data is null) return false;
        if (!PluginMailActionArgs.TryIdList(PluginMailArgs.Prop(data, "emailIds"), PluginMailActionArgs.MaxBatchIds, out var ids)) return false;
        if (PluginMailArgs.Prop(data, "action") is not { Kind: PluginValueKind.String } action || !Actions.Contains(action.StringValue, StringComparer.Ordinal)) return false;
        var mailbox = PluginMailArgs.Prop(data, "mailboxId");
        var tag = PluginMailArgs.Prop(data, "tagId");
        var kind = action.StringValue;
        var tagging = Is(kind, "tag") || Is(kind, "untag");
        if (!Is(kind, "move") && mailbox is not null) return false;
        if (!tagging && tag is not null) return false;
        if (Is(kind, "move") && !PluginMailArgs.IsId(mailbox)) return false;
        if (tagging && (tag is not { Kind: PluginValueKind.String } || tag.StringValue.Trim().Length is 0 or > 100)) return false;
        batch = new(ids, kind, Is(kind, "move") ? mailbox!.StringValue : "", tagging ? tag!.StringValue.Trim() : "");
        return true;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = TryRead(PluginMailArgs.At(arguments, 0), out var batch);
        var session = capture!.Session;
        var client = session.Client;
        var target = "";
        if (batch.Moving)
        {
            var (early, folder) = await TargetAsync(context, session.Client, batch, ct).ConfigureAwait(false);
            if (early is not null) return early;
            target = folder;
        }

        if (batch.Tagging && !Host.Tags.Ids().Contains(batch.TagId, StringComparer.Ordinal)) return Fail(PluginErrorCode.InvalidArguments);
        if (batch.Moving && !Within(context, "email.move", MovesPerMinute)) return Fail(PluginErrorCode.RateLimited);

        // the messages as the server has them now, from the signed-in account only
        var get = await ReaderCalls.CallAsync(client, "Email/get", new JsonObject
        {
            ["accountId"] = client.Session.MailAccountId,
            ["ids"] = new JsonArray([.. batch.Ids.Select(id => (JsonNode)JsonValue.Create(id)!)]),
            ["properties"] = new JsonArray("id", "mailboxIds", "keywords"),
        }, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!get.IsOk) return Fail(PluginErrorCode.Failed);
        var found = (get.Value!["list"] as JsonArray ?? []).OfType<JsonObject>().Where(e => e.Str("id") is { } id && batch.Ids.Contains(id, StringComparer.Ordinal)).ToList();
        if (found.Count == 0) return Answer(0, batch.Ids.Count, null);

        var writer = Host.Writers.ForSession(session);
        var restore = new PluginBatchRestore();
        var applied = batch.Moving
            ? await MoveAsync(context, writer, batch, target, found, restore, ct).ConfigureAwait(false)
            : await KeywordsAsync(context, writer, batch, found, restore, ct).ConfigureAwait(false);
        if (applied.Early is not null) return applied.Early;
        if (applied.Count == 0 && batch.Moving) return Answer(0, batch.Ids.Count, null);

        var pluginId = context.Binding.Package.Id;
        if (Host.History is { } history)
        {
            restore.EntryId = history.Record($"Plugin {pluginId}: {applied.Label} messages", async undoCt => await restore.RunOnceAsync(undoCt).ConfigureAwait(false)
                ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("The batch could not be put back.")));
        }

        var token = Host.Undo.Issue(pluginId, capture.LocalAccountKey, UndoKind, new JsonObject { ["restore"] = Host.Keep(restore) });
        return Answer(applied.Count, batch.Ids.Count - applied.Count, token);
    }

    /// <summary>The folder a move or archive goes to: the Archive folder, or an own folder that is not the trash and that nobody else reads.</summary>
    private static async Task<(PluginCallOutcome? Early, string Folder)> TargetAsync(IPluginInvocationContext context, Jmap.IJmapClient client, Batch batch, CancellationToken ct)
    {
        var boxes = await FilterAccess.MailboxesAsync(client, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return (Stale(context), "");
        if (!boxes.IsOk) return (Fail(PluginErrorCode.Failed), "");
        if (Is(batch.Action, "archive"))
        {
            var archive = boxes.Value!.FirstOrDefault(m => string.Equals(m.Str("role"), "archive", StringComparison.Ordinal))?.Str("id");
            return string.IsNullOrEmpty(archive) ? (Fail(PluginErrorCode.Unavailable), "") : (null, archive);
        }

        var box = boxes.Value!.FirstOrDefault(m => string.Equals(m.Str("id"), batch.MailboxId, StringComparison.Ordinal));
        if (box is null || string.Equals(box.Str("role"), "trash", StringComparison.Ordinal)) return (Fail(PluginErrorCode.InvalidArguments), "");
        // the share map is not in the folder list: ask the server for the folder and those above it
        var shared = await PluginRulesAccess.IsSharedOutAsync(client, boxes.Value!, batch.MailboxId, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return (Stale(context), "");
        return shared ? (Fail(PluginErrorCode.InvalidArguments), "") : (null, batch.MailboxId);
    }

    private async Task<Applied> MoveAsync(IPluginInvocationContext context, IMailWriter writer, Batch batch, string target, List<JsonObject> found, PluginBatchRestore restore, CancellationToken ct)
    {
        var archive = Is(batch.Action, "archive");
        var ids = found.Select(e => e.Str("id")!).ToList();
        var hooks = Host.Hooks;
        if (hooks is not null && !await AllowedAsync(hooks, archive, ids, target, ct).ConfigureAwait(false)) return new(Answer(0, batch.Ids.Count, null), 0, "");
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return new(Stale(context), 0, "");
        var moved = await writer.MoveAsync(ids, null, target, ct).ConfigureAwait(false);
        var updated = moved.Value?.Updated ?? [];
        if (updated.Count == 0) return new(null, 0, "");
        var before = found.Where(e => updated.Contains(e.Str("id")!, StringComparer.Ordinal))
            .ToDictionary(e => e.Str("id")!, e => (IReadOnlyDictionary<string, bool>)TrueKeys(e["mailboxIds"]), StringComparer.Ordinal);
        restore.Undo = async undoCt => Accepted(await writer.SetMailboxIdsAsync(before, undoCt).ConfigureAwait(false));
        if (hooks is not null) await TellAsync(hooks, archive, updated, target, ct).ConfigureAwait(false);
        return new(null, updated.Count, $"{batch.Action} {updated.Count}");
    }

    private static async Task<Applied> KeywordsAsync(IPluginInvocationContext context, IMailWriter writer, Batch batch, List<JsonObject> found, PluginBatchRestore restore, CancellationToken ct)
    {
        var patch = new Dictionary<string, bool>(StringComparer.Ordinal);
        var undoPatch = new Dictionary<string, bool>(StringComparer.Ordinal);
        List<string> changed;
        if (Is(batch.Action, "read") || Is(batch.Action, "unread"))
        {
            var read = Is(batch.Action, "read");
            changed = [.. found.Where(e => IsSet(e["keywords"], "$seen") != read).Select(e => e.Str("id")!)];
            patch["$seen"] = read;
            undoPatch["$seen"] = !read;
        }
        else
        {
            var add = Is(batch.Action, "tag");
            var keyword = "$label:" + batch.TagId;
            var legacy = "$color:" + batch.TagId;
            changed = [.. found.Where(e => (IsSet(e["keywords"], keyword) || IsSet(e["keywords"], legacy)) != add).Select(e => e.Str("id")!)];
            patch[keyword] = add;
            if (!add) patch[legacy] = false;
            undoPatch[keyword] = !add;
        }

        IReadOnlyList<string> updated = changed;
        if (changed.Count > 0)
        {
            if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return new(Stale(context), 0, "");
            var set = await writer.UpdateKeywordsAsync(changed, patch, ct).ConfigureAwait(false);
            updated = set.Value?.Updated ?? [];
            if (!Accepted(set) && updated.Count == 0) return new(Answer(0, batch.Ids.Count, null), 0, "");
        }

        var done = updated.ToList();
        restore.Undo = async undoCt => done.Count == 0 || Accepted(await writer.UpdateKeywordsAsync(done, undoPatch, undoCt).ConfigureAwait(false));
        return new(null, found.Count, $"{batch.Action} {found.Count}");
    }

    private static PluginCallOutcome Answer(int done, int failed, string? token)
    {
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("done", b.Number(done)), ("failed", b.Number(failed)), ("undoToken", token is null ? b.Null() : b.String(token))));
    }

    private static Dictionary<string, bool> TrueKeys(JsonNode? map) =>
        map is JsonObject o ? o.Where(p => p.Value is JsonValue v && v.TryGetValue<bool>(out var on) && on).ToDictionary(p => p.Key, _ => true, StringComparer.Ordinal) : new(StringComparer.Ordinal);

    private static bool IsSet(JsonNode? map, string keyword) => map is JsonObject o && o[keyword] is JsonValue v && v.TryGetValue<bool>(out var on) && on;

    private static async Task<bool> AllowedAsync(IHookBus hooks, bool archive, IReadOnlyList<string> ids, string to, CancellationToken ct)
    {
        var hook = archive ? HookNames.OnBeforeEmailArchive : HookNames.OnBeforeEmailMove;
        if (!hooks.HasHandlers(hook)) return true;
        return archive
            ? await hooks.InterceptAsync(hook, new EmailIdsPayload(ids), ct).ConfigureAwait(false)
            : await hooks.InterceptAsync(hook, new EmailMovePayload(ids, null, to), ct).ConfigureAwait(false);
    }

    private static async Task TellAsync(IHookBus hooks, bool archive, IReadOnlyList<string> ids, string to, CancellationToken ct)
    {
        var hook = archive ? HookNames.OnEmailArchive : HookNames.OnAfterEmailMove;
        if (!hooks.HasHandlers(hook)) return;
        if (archive) await hooks.NotifyAsync(hook, new EmailIdsPayload(ids), ct).ConfigureAwait(false);
        else await hooks.NotifyAsync(hook, new EmailMovePayload(ids, null, to), ct).ConfigureAwait(false);
    }
}
