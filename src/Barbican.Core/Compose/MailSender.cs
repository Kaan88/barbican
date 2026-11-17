// Mirrors: Rust jmap.rs:916 send_message (Email/set create + destroy of the replaced draft, EmailSubmission/set with the identity, the explicit envelope, HOLDFOR, onSuccessUpdateEmail; the forbiddenFrom retry; $answered / $forwarded), :1064 cancel_submission,
// compose.rs:868-880 send; Bulwark lib/jmap/client.ts sendEmail :5560, cancelEmailSubmission, components/email/message-actions.ts (undo toast, Send now), lib/plugin-hook-catalog.ts:108-111 (onBeforeEmailSend, onTransformOutgoingEmail, onComposeSend, onAfterEmailSend);
// RFC 8621 section 7.5 (EmailSubmission/set, undoStatus, onSuccessUpdateEmail), RFC 4865 (FUTURERELEASE / HOLDFOR); Stalwart crates/jmap/src/submission/set.rs (HOLDFOR in the envelope parameters; forbiddenFrom for a MAIL FROM the identity does not own).
// Decisions: D-M3A-02 (onComposeSend is an intercept: false means a plugin sent the message itself), D-M3A-03 (Send now = cancel and resubmit without a hold; M3 plan section 9 item 6), D-M3A-04 (a failed send takes its server copy out again, so no stray draft is left).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Compose;

/// <summary>Submits a composed message and undoes or releases a held one.</summary>
public sealed class MailSender(IAccountManager accounts, IIdentityService identities, IComposeSeeder seeder, IHookBus hooks, IDocCodec codec, TimeProvider time) : IMailSender
{
    public async Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(options);
        if (DraftBuilder.Recipients(draft).Count == 0)
        {
            return Fail("Add at least one recipient.");
        }

        var ctx = ComposeContext.For(accounts, draft.AccountId);
        if (!ctx.IsOk)
        {
            return Result<SendReceipt>.Fail(ctx.Error!);
        }

        var ids = await identities.ListAsync(draft.AccountId, ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<SendReceipt>.Fail(ids.Error!);
        }

        var identity = SubmissionRules.ChooseIdentity([.. ids.Value!.Select(i => i.Identity)], draft.IdentityId, draft.FromOverride?.Email);
        if (identity is null)
        {
            return Fail(draft.FromOverride is { } o && o.Email.Contains('@', StringComparison.Ordinal) ? $"No identity of this account may send as {o.Email.Trim()}." : "No sending identity on this account.");
        }

        var hold = Hold(ctx.Value!, options);
        if (!hold.IsOk)
        {
            return Result<SendReceipt>.Fail(hold.Error!);
        }

        var hooked = await RunHooksAsync(draft, identity, options, ct).ConfigureAwait(false);
        if (!hooked.IsOk)
        {
            return Result<SendReceipt>.Fail(hooked.Error!);
        }

        var (sendDraft, html, text) = hooked.Value;
        var sent = await SubmitAsync(ctx.Value!, sendDraft, identity, hold.Value, html, text, ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<SendReceipt>.Fail(sent.Error!);
        }

        var receipt = sent.Value! with
        {
            ReleaseAt = hold.Value is { } h and > 0 ? time.GetUtcNow().AddSeconds(h) : null,
            CanUndo = hold.Value is > 0,
            Draft = draft,
        };
        if (hooks.HasHandlers(HookNames.OnAfterEmailSend))
        {
            hooks.Fire(HookNames.OnAfterEmailSend, () => new EmailSentPayload(receipt.EmailId, receipt.AccountId, receipt.SubmissionId, sendDraft.To, sendDraft.Cc, sendDraft.Bcc, sendDraft.Subject, options.ScheduledAt));
        }

        return Result<SendReceipt>.Ok(receipt);
    }

    public async Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.CanUndo)
        {
            return Result<ComposeSeed>.Fail(new ValidationError("This message can no longer be undone."));
        }

        var ctx = ComposeContext.For(accounts, receipt.AccountId);
        if (!ctx.IsOk)
        {
            return Result<ComposeSeed>.Fail(ctx.Error!);
        }

        var cancelled = await SubmissionOps.CancelAsync(ctx.Value!, receipt.SubmissionId, ct).ConfigureAwait(false);
        if (!cancelled.IsOk)
        {
            return Result<ComposeSeed>.Fail(cancelled.Error!);
        }

        var emailId = cancelled.Value ?? receipt.EmailId;
        if (await SubmissionOps.RestoreDraftAsync(ctx.Value!, emailId, ct).ConfigureAwait(false))
        {
            var seeded = await seeder.SeedAsync(new ComposeRequest(receipt.AccountId, ComposeKind.EditDraft) { SourceEmailId = emailId, Plain = receipt.Draft?.PlainText ?? false }, ct).ConfigureAwait(false);
            if (seeded.IsOk)
            {
                return seeded;
            }
        }

        // the server copy could not go back to Drafts: take the never-sent copy out so it cannot pass for sent mail; the composer's content is the receipt's (Rust cancel_submission)
        _ = await ReaderCallsDestroyAsync(ctx.Value!, emailId, ct).ConfigureAwait(false);
        return receipt.Draft is { } d
            ? Result<ComposeSeed>.Ok(SeedOf(d))
            : Result<ComposeSeed>.Fail(new ValidationError("The message was not sent, but it could not be reopened."));
    }

    public async Task<Result<Unit>> SendNowAsync(SendReceipt receipt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var ctx = ComposeContext.For(accounts, receipt.AccountId);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var got = await SubmissionOps.GetAsync(ctx.Value!, receipt.SubmissionId, ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<Unit>.Fail(got.Error!);
        }

        var sub = got.Value;
        if (sub is null)
        {
            return Result<Unit>.Fail(new ValidationError("This message is no longer held."));
        }

        if (!string.Equals(sub.Str("undoStatus"), "pending", StringComparison.Ordinal))
        {
            return Result<Unit>.Fail(new ValidationError("The message has already been sent."));
        }

        var emailId = sub.Str("emailId") ?? receipt.EmailId;
        var envelope = await SubmissionOps.EnvelopeOfAsync(ctx.Value!, sub, emailId, ct).ConfigureAwait(false);
        if (!envelope.IsOk)
        {
            return Result<Unit>.Fail(envelope.Error!);
        }

        var cancelled = await SubmissionOps.CancelAsync(ctx.Value!, receipt.SubmissionId, ct).ConfigureAwait(false);
        if (!cancelled.IsOk)
        {
            return Result<Unit>.Fail(cancelled.Error!);
        }

        var roles = await ctx.Value!.RolesAsync(false, ct).ConfigureAwait(false);
        var again = await SubmissionOps.CreateAsync(ctx.Value, emailId, sub.Str("identityId") ?? "", envelope.Value.From, envelope.Value.Recipients, null, roles.IsOk ? roles.Value!.Sent : null, ct).ConfigureAwait(false);
        if (again.IsOk)
        {
            return Result<Unit>.Ok(default);
        }

        // nothing is held any more and nothing went out: the message goes back to Drafts, so it does not sit in Sent looking sent
        var restored = await SubmissionOps.RestoreDraftAsync(ctx.Value, emailId, ct).ConfigureAwait(false);
        return Result<Unit>.Fail(new ValidationError($"Send now failed: {again.Error!.Message}" + (restored ? " The message is back in Drafts." : string.Empty)));
    }

    /// <summary>The composer's content as a seed (an undo that could not restore the server copy).</summary>
    internal static ComposeSeed SeedOf(ComposeDraft d) => new()
    {
        AccountId = d.AccountId,
        Kind = ComposeKind.EditDraft,
        To = d.To,
        Cc = d.Cc,
        Bcc = d.Bcc,
        Subject = d.Subject,
        Body = d.Body,
        IdentityId = d.IdentityId,
        FromOverride = d.FromOverride,
        Attachments = d.Attachments,
        SourceEmailId = d.SourceEmailId,
        InReplyTo = d.InReplyTo,
        References = d.References,
    };

    private static Result<SendReceipt> Fail(string text) => Result<SendReceipt>.Fail(new ValidationError(text));

    private static async Task<Result<Unit>> ReaderCallsDestroyAsync(ComposeContext ctx, string emailId, CancellationToken ct)
    {
        var r = await ReaderCalls.CallAsync(ctx.Client, "Email/set", new JsonObject { ["accountId"] = ctx.Account, ["destroy"] = new JsonArray(emailId) }, ct).ConfigureAwait(false);
        return r.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(r.Error!);
    }

    /// <summary>HOLDFOR seconds: a scheduled time, else the undo window clamped to the account's limit; none without FUTURERELEASE (RFC 4865).</summary>
    private Result<long?> Hold(ComposeContext ctx, SendOptions options)
    {
        var max = SubmissionOps.MaxDelayedSend(ctx);
        if (options.ScheduledAt is { } at)
        {
            var secs = DelayedSend.ScheduledHold(at.ToUnixTimeMilliseconds(), time.GetUtcNow().ToUnixTimeMilliseconds(), max);
            return secs.IsOk ? Result<long?>.Ok(secs.Value) : Result<long?>.Fail(secs.Error!);
        }

        return Result<long?>.Ok(options.UndoSeconds > 0 && max > 0 ? Math.Min(options.UndoSeconds, max) : null);
    }

    private async Task<Result<(ComposeDraft Draft, string Html, string Text)>> RunHooksAsync(ComposeDraft draft, Identity identity, SendOptions options, CancellationToken ct)
    {
        var html = draft.PlainText ? "" : codec.Export(draft.Body);
        var text = codec.ToText(draft.Body);
        var from = SubmissionRules.HeaderFrom(DraftBuilder.Outgoing(draft), identity, "").Email;
        var outgoing = new OutgoingEmailPayload(draft.AccountId, identity.Id, from, draft.To, draft.Cc, draft.Bcc, draft.Subject, html, text)
        {
            AttachmentNames = [.. draft.Attachments.Where(a => a.Cid is null).Select(a => a.Name)],
            ReadReceipt = draft.ReadReceipt,
            ScheduledFor = options.ScheduledAt,
        };

        // fail-closed (R2-10): a handler that fails or times out stops the send, since sending cannot be taken back
        if (!await hooks.AllowAsync(HookNames.OnBeforeEmailSend, () => outgoing, ct).ConfigureAwait(false))
        {
            return Result<(ComposeDraft, string, string)>.Fail(new ValidationError("A plugin stopped this message from being sent."));
        }

        if (!await hooks.AllowAsync(HookNames.OnComposeSend, () => new ComposeSendRequestPayload(outgoing, options.UndoSeconds, options.ScheduledAt), ct).ConfigureAwait(false))
        {
            return Result<(ComposeDraft, string, string)>.Fail(new ValidationError("A plugin took over sending this message."));
        }

        if (hooks.HasHandlers(HookNames.OnTransformOutgoingEmail))
        {
            var changed = await hooks.TransformAsync(HookNames.OnTransformOutgoingEmail, outgoing, ct).ConfigureAwait(false);
            draft = draft with { To = changed.To, Cc = changed.Cc, Bcc = changed.Bcc, Subject = changed.Subject };
            html = draft.PlainText ? "" : changed.Html;
            text = changed.Text;
        }

        return Result<(ComposeDraft, string, string)>.Ok((draft, html, text));
    }

    private static async Task<Result<SendReceipt>> SubmitAsync(ComposeContext ctx, ComposeDraft draft, Identity identity, long? hold, string html, string text, CancellationToken ct)
    {
        var roles = await ctx.RolesAsync(false, ct).ConfigureAwait(false);
        if (!roles.IsOk)
        {
            return Result<SendReceipt>.Fail(roles.Error!);
        }

        var recipients = DraftBuilder.Recipients(draft);

        // the MAIL FROM the server derives from the identity, and the one asked for: the From override, so the Return-Path does not give the identity's address away (Bulwark #1009)
        var identityMailFrom = SubmissionRules.HeaderFrom(DraftBuilder.Outgoing(draft) with { FromOverride = null }, identity, ctx.Username).Email;
        var mailFrom = draft.FromOverride is { } o && o.Email.Contains('@', StringComparison.Ordinal) ? o.Email.Trim() : identityMailFrom;
        JsonObject Submission(string emailId, string from)
        {
            var sub = new JsonObject { ["emailId"] = emailId, ["identityId"] = identity.Id };
            if (SubmissionRules.Envelope(from, identityMailFrom, hold, recipients) is { } env)
            {
                sub["envelope"] = EnvelopeJson(env);
            }

            return sub;
        }

        var onSuccess = OnSuccess(roles.Value!);
        var response = await ctx.Client.SendAsync(JmapRequest.Of([.. Calls(ctx, draft, identity, roles.Value!.Drafts, html, text, Submission("#d", mailFrom), onSuccess)]), ct).ConfigureAwait(false);
        if (!response.IsOk)
        {
            return Result<SendReceipt>.Fail(response.Error!);
        }

        var emailArgs = response.Value!.Args("e", "Email/set");
        var copy = emailArgs.IsOk ? ReaderCalls.Created(emailArgs.Value!, "d") : Result<string>.Fail(emailArgs.Error!);
        if (!copy.IsOk)
        {
            return Result<SendReceipt>.Fail(SubmissionOps.Plain(copy.Error!));
        }

        var subArgs = response.Value.Args("s", "EmailSubmission/set");
        var created = subArgs.IsOk ? ReaderCalls.Created(subArgs.Value!, "s") : Result<string>.Fail(subArgs.Error!);
        if (created.IsOk)
        {
            return Result<SendReceipt>.Ok(new SendReceipt(draft.AccountId, copy.Value!, created.Value!));
        }

        // a server that only takes the identity's own address as MAIL FROM (RFC 8621 7.5 forbiddenMailFrom; Stalwart: forbiddenFrom): the copy in Drafts is submitted once more with the identity's address (Bulwark sendEmail)
        if (created.Error is SetError e && SubmissionRules.MailFromRefused(e) && !string.Equals(mailFrom, identityMailFrom, StringComparison.OrdinalIgnoreCase))
        {
            var again = await ResubmitAsync(ctx, Submission(copy.Value!, identityMailFrom), onSuccess, ct).ConfigureAwait(false);
            if (again.IsOk)
            {
                return Result<SendReceipt>.Ok(new SendReceipt(draft.AccountId, copy.Value!, again.Value!) { EnvelopeRewritten = true });
            }

            created = again;
        }

        // nothing went out: the copy made for the send comes out of Drafts again (the composer keeps the content and saves a fresh draft)
        _ = await ReaderCallsDestroyAsync(ctx, copy.Value!, ct).ConfigureAwait(false);
        return Result<SendReceipt>.Fail(SubmissionOps.Plain(created.Error!));
    }

    /// <summary>onSuccessUpdateEmail: when the server has the message (a held one included) it leaves Drafts for Sent and drops <c>$draft</c> (RFC 8621 7.5).</summary>
    private static JsonObject OnSuccess(MailboxRoles roles)
    {
        var filed = new JsonObject { ["mailboxIds/" + roles.Drafts] = null, ["keywords/$draft"] = null };
        if (roles.Sent is { } sentId)
        {
            filed["mailboxIds/" + sentId] = true;
        }

        return new JsonObject { ["#s"] = filed };
    }

    private static async Task<Result<string>> ResubmitAsync(ComposeContext ctx, JsonObject submission, JsonObject onSuccess, CancellationToken ct)
    {
        var retry = await ReaderCalls.CallAsync(
            ctx.Client,
            "EmailSubmission/set",
            new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["create"] = new JsonObject { ["s"] = submission }, ["onSuccessUpdateEmail"] = onSuccess.DeepClone() },
            ct).ConfigureAwait(false);
        return retry.IsOk ? ReaderCalls.Created(retry.Value!, "s") : Result<string>.Fail(retry.Error!);
    }

    private static List<JsonArray> Calls(ComposeContext ctx, ComposeDraft draft, Identity identity, string draftsId, string html, string text, JsonObject submission, JsonObject onSuccess)
    {
        var emailSet = new JsonObject
        {
            ["accountId"] = ctx.Account,
            ["create"] = new JsonObject { ["d"] = DraftBuilder.Build(draft, identity, ctx.Username, draftsId, true, html, text) },
        };
        if (draft.DraftId is { Length: > 0 } old)
        {
            emailSet["destroy"] = new JsonArray(old);
        }

        var calls = new List<JsonArray>
        {
            JmapRequest.Call("Email/set", emailSet, "e"),
            JmapRequest.Call("EmailSubmission/set", new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["create"] = new JsonObject { ["s"] = submission }, ["onSuccessUpdateEmail"] = onSuccess.DeepClone() }, "s"),
        };
        if (draft.SourceEmailId is { Length: > 0 } source && draft.Kind is ComposeKind.Reply or ComposeKind.ReplyAll or ComposeKind.Forward or ComposeKind.ForwardAsAttachment)
        {
            var keyword = draft.Kind is ComposeKind.Forward or ComposeKind.ForwardAsAttachment ? "$forwarded" : "$answered";
            calls.Add(JmapRequest.Call("Email/set", new JsonObject { ["accountId"] = ctx.Account, ["update"] = new JsonObject { [source] = new JsonObject { ["keywords/" + keyword] = true } } }, "a"));
        }

        return calls;
    }

    private static JsonObject EnvelopeJson(SubmissionEnvelope env)
    {
        static JsonObject Addr(EnvelopeAddress a)
        {
            var o = new JsonObject { ["email"] = a.Email };
            if (a.Parameters is { Count: > 0 } p)
            {
                o["parameters"] = new JsonObject(p.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)));
            }

            return o;
        }

        return new JsonObject { ["mailFrom"] = Addr(env.MailFrom), ["rcptTo"] = new JsonArray([.. env.RcptTo.Select(a => (JsonNode?)Addr(a))]) };
    }
}
