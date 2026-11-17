// Mirrors: Rust jmap.rs:903 save_draft (create the new draft and destroy the replaced one in one Email/set), compose.rs autosave_now / discard (:1120); Bulwark lib/jmap/client.ts createDraft/updateDraft (the old draft is destroyed in the same request),
// components/email/email-composer.tsx autosave; lib/plugin-hook-catalog.ts:94-95 (onBeforeDraftAutoSave, onDraftAutoSave); RFC 8621 section 4.6 (Email/set). Stalwart crates/jmap/src/email/set.rs (create with $draft; keywords validated).
// Decision D-246: a save with no resolvable identity is an error, not a draft from the first identity (Rust pick_identity_for).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Compose;

/// <summary>Saves and removes drafts: one Email/set creates the new draft and destroys the previous, so two saves never leave two drafts.</summary>
public sealed class DraftService(IAccountManager accounts, IIdentityService identities, IHookBus hooks, IDocCodec codec) : IDraftService
{
    public Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, CancellationToken ct) => SaveAsync(draft, false, ct);

    public async Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, bool autosave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (hooks.HasHandlers(HookNames.OnBeforeDraftAutoSave))
        {
            var html = draft.PlainText ? "" : codec.Export(draft.Body);
            var before = new AlmostSavedDraftPayload(draft.AccountId, draft.DraftId, draft.To, draft.Cc, draft.Bcc, draft.Subject, html, codec.ToText(draft.Body));
            var changed = await hooks.TransformAsync(HookNames.OnBeforeDraftAutoSave, before, ct).ConfigureAwait(false);

            // Bulwark: a plugin may skip a save while typing; the saves on close and before send always run
            if (changed.Skip && autosave)
            {
                return Result<SavedDraft>.Ok(new SavedDraft(draft.DraftId ?? ""));
            }

            draft = draft with { To = changed.To, Cc = changed.Cc, Bcc = changed.Bcc, Subject = changed.Subject };
        }

        var saved = await CreateAsync(draft, ct).ConfigureAwait(false);
        if (saved.IsOk && hooks.HasHandlers(HookNames.OnDraftAutoSave))
        {
            var id = saved.Value!.DraftId;
            var subject = draft.Subject;
            hooks.Fire(HookNames.OnDraftAutoSave, () => new DraftSavedPayload(id, subject));
        }

        return saved;
    }

    public async Task<Result<Unit>> DiscardAsync(string accountId, string draftId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(draftId);
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var r = await ReaderCalls.CallAsync(ctx.Value!.Client, "Email/set", new JsonObject { ["accountId"] = ctx.Value.Account, ["destroy"] = new JsonArray(draftId) }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        // already gone is as good as destroyed
        var failed = SetErrors.Read(r.Value).Failed.FirstOrDefault(f => !string.Equals(f.Type, "notFound", StringComparison.Ordinal));
        return failed is null ? Result<Unit>.Ok(default) : Result<Unit>.Fail(failed);
    }

    private async Task<Result<SavedDraft>> CreateAsync(ComposeDraft draft, CancellationToken ct)
    {
        var ctx = ComposeContext.For(accounts, draft.AccountId);
        if (!ctx.IsOk)
        {
            return Result<SavedDraft>.Fail(ctx.Error!);
        }

        var ids = await identities.ListAsync(draft.AccountId, ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<SavedDraft>.Fail(ids.Error!);
        }

        var identity = SubmissionRules.ChooseIdentity([.. ids.Value!.Select(i => i.Identity)], draft.IdentityId, draft.FromOverride?.Email);
        if (identity is null)
        {
            return Result<SavedDraft>.Fail(new ValidationError(draft.FromOverride is { } o && o.Email.Contains('@', StringComparison.Ordinal) ? $"No identity of this account may send as {o.Email.Trim()}." : "No sending identity on this account."));
        }

        var html = draft.PlainText ? "" : codec.Export(draft.Body);
        var text = codec.ToText(draft.Body);
        for (var attempt = 0; ; attempt++)
        {
            var roles = await ctx.Value!.RolesAsync(attempt > 0, ct).ConfigureAwait(false);
            if (!roles.IsOk)
            {
                return Result<SavedDraft>.Fail(roles.Error!);
            }

            var args = new JsonObject
            {
                ["accountId"] = ctx.Value.Account,
                ["create"] = new JsonObject { ["d"] = DraftBuilder.Build(draft, identity, ctx.Value.Username, roles.Value!.Drafts, true, html, text) },
            };
            if (draft.DraftId is { Length: > 0 } old)
            {
                args["destroy"] = new JsonArray(old);
            }

            var r = await ReaderCalls.CallAsync(ctx.Value.Client, "Email/set", args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<SavedDraft>.Fail(r.Error!);
            }

            var created = ReaderCalls.Created(r.Value!, "d");
            if (created.IsOk)
            {
                return Result<SavedDraft>.Ok(new SavedDraft(created.Value!));
            }

            // a folder the cache still names may be gone: one retry with fresh roles
            if (attempt == 0 && created.Error is SetError { Type: "invalidProperties" })
            {
                continue;
            }

            return Result<SavedDraft>.Fail(SubmissionOps.Plain(created.Error!));
        }
    }
}
