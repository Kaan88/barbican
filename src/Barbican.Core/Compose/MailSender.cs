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
public sealed class MailSender(IAccountManager accounts, IIdentityService identities, IComposeSeeder seeder, IHookBus hooks, IDocCodec codec, TimeProvider time, IOutgoingAddOnHooks? addOns = null) : IMailSender
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

        var addOnSend = addOns?.Start(ctx.Value!.Session);
        var hooked = await RunHooksAsync(draft, identity, options, addOnSend, ids.Value!, ct).ConfigureAwait(false);
        if (!hooked.IsOk)
        {
            return Result<SendReceipt>.Fail(hooked.Error!);
        }

        var (sendDraft, html, text) = hooked.Value;
        var sent = await SubmitAsync(ctx.Value!, sendDraft, identity, hold.Value, html, text, addOnSend, ct).ConfigureAwait(false);
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

        var guard = AccountGuard(ctx.Value!, receipt.AccountId);
        var cancelled = await SubmissionOps.CancelAsync(ctx.Value!, receipt.SubmissionId, ct, guard).ConfigureAwait(false);
        if (!cancelled.IsOk)
        {
            return Result<ComposeSeed>.Fail(cancelled.Error!);
        }

        var emailId = cancelled.Value ?? receipt.EmailId;
        if (await SubmissionOps.RestoreDraftAsync(ctx.Value!, emailId, ct, guard).ConfigureAwait(false))
        {
            var seeded = await seeder.SeedAsync(new ComposeRequest(receipt.AccountId, ComposeKind.EditDraft) { SourceEmailId = emailId, Plain = receipt.Draft?.PlainText ?? false }, ct).ConfigureAwait(false);
            if (seeded.IsOk)
            {
                return seeded;
            }
        }

        // the server copy could not go back to Drafts: take the never-sent copy out so it cannot pass for sent mail; the composer's content is the receipt's (Rust cancel_submission)
        _ = await SubmissionOps.DestroyEmailAsync(ctx.Value!, emailId, ct, guard).ConfigureAwait(false);
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

        var guard = AccountGuard(ctx.Value!, receipt.AccountId);
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

        var cancelled = await SubmissionOps.CancelAsync(ctx.Value!, receipt.SubmissionId, ct, guard).ConfigureAwait(false);
        if (!cancelled.IsOk)
        {
            return Result<Unit>.Fail(cancelled.Error!);
        }

        var roles = await ctx.Value!.RolesAsync(false, ct).ConfigureAwait(false);
        var again = await SubmissionOps.CreateAsync(ctx.Value, emailId, sub.Str("identityId") ?? "", envelope.Value.From, envelope.Value.Recipients, null, roles.IsOk ? roles.Value!.Sent : null, ct, guard).ConfigureAwait(false);
        if (again.IsOk)
        {
            return Result<Unit>.Ok(default);
        }

        // nothing is held any more and nothing went out: the message goes back to Drafts, so it does not sit in Sent looking sent
        var restored = await SubmissionOps.RestoreDraftAsync(ctx.Value, emailId, ct, guard).ConfigureAwait(false);
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
        Keywords = d.Keywords,
    };

    private static Result<SendReceipt> Fail(string text) => Result<SendReceipt>.Fail(new ValidationError(text));

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

    private async Task<Result<(ComposeDraft Draft, string Html, string Text)>> RunHooksAsync(ComposeDraft draft, Identity identity, SendOptions options, IOutgoingAddOnSend? addOnSend, IReadOnlyList<ComposeIdentity> identities, CancellationToken ct)
    {
        var html = draft.PlainText ? "" : codec.Export(draft.Body);
        var text = codec.ToText(draft.Body);
        var from = SubmissionRules.HeaderFrom(DraftBuilder.Outgoing(draft), identity, "").Email;
        var outgoing = new OutgoingEmailPayload(draft.AccountId, identity.Id, from, draft.To, draft.Cc, draft.Bcc, draft.Subject, html, text)
        {
            ComposerId = draft.ComposerId,
            AuthoredText = draft.AuthoredText,
            IsQuickReply = draft.IsQuickReply,
            ExternalRecipients = ExternalRecipients(draft, identities),
            Kind = draft.Kind,
            InReplyTo = draft.InReplyTo,
            Attachments = [.. draft.Attachments],
            Keywords = OutgoingKeywords.Clean(draft.Keywords),
            AttachmentNames = [.. draft.Attachments.Where(a => a.Cid is null).Select(a => a.Name)],
            ReadReceipt = draft.ReadReceipt,
            ScheduledFor = options.ScheduledAt,
        };

        // fail-closed (R2-10): a handler that fails or times out stops the send, since sending cannot be taken back
        async Task<bool> AllowedAsync(OutgoingEmailPayload check) =>
            await hooks.AllowAsync(HookNames.OnBeforeEmailSend, () => check, ct).ConfigureAwait(false) &&
                (addOnSend is null || await addOnSend.CheckAsync(check, ct).ConfigureAwait(false));
        if (!await AllowedAsync(outgoing).ConfigureAwait(false))
        {
            return Result<(ComposeDraft, string, string)>.Fail(new ValidationError("A plugin stopped this message from being sent."));
        }

        if (!await hooks.AllowAsync(HookNames.OnComposeSend, () => new ComposeSendRequestPayload(outgoing, options.UndoSeconds, options.ScheduledAt), ct).ConfigureAwait(false))
        {
            return Result<(ComposeDraft, string, string)>.Fail(new ValidationError("A plugin took over sending this message."));
        }

        var changed = hooks.HasHandlers(HookNames.OnTransformOutgoingEmail)
            ? await hooks.TransformAsync(HookNames.OnTransformOutgoingEmail, outgoing, ct).ConfigureAwait(false) : outgoing;
        if (addOnSend is not null) changed = await addOnSend.TransformAsync(changed with
        {
            AccountId = outgoing.AccountId, IdentityId = outgoing.IdentityId, ComposerId = outgoing.ComposerId,
        }, ct).ConfigureAwait(false);
        var finalDraft = draft with
        {
            To = changed.To, Cc = changed.Cc, Bcc = changed.Bcc, Subject = changed.Subject,
            Keywords = OutgoingKeywords.Clean(outgoing.Keywords.Concat(changed.Keywords)),
        };
        var targets = DraftBuilder.Recipients(finalDraft);
        if (targets.Count == 0)
            return Result<(ComposeDraft, string, string)>.Fail(new ValidationError("Add at least one recipient."));
        if (!targets.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                DraftBuilder.Recipients(draft).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) &&
            !await AllowedAsync(outgoing with
            {
                To = finalDraft.To, Cc = finalDraft.Cc, Bcc = finalDraft.Bcc,
                ExternalRecipients = ExternalRecipients(finalDraft, identities),
            }).ConfigureAwait(false))
            return Result<(ComposeDraft, string, string)>.Fail(new ValidationError("A plugin stopped this message from being sent."));

        html = finalDraft.PlainText ? "" : changed.Html;
        text = changed.Text;
        return Result<(ComposeDraft, string, string)>.Ok((finalDraft, html, text));
    }

    private static IReadOnlyList<string> ExternalRecipients(ComposeDraft draft, IReadOnlyList<ComposeIdentity> identities)
    {
        var domains = identities.Select(i => i.Identity.Email).Select(email => email.LastIndexOf('@') is var at && at >= 0 ? email[(at + 1)..] : "")
            .Where(domain => domain.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. DraftBuilder.Recipients(draft).Where(email => email.LastIndexOf('@') is var at && at >= 0 && !domains.Contains(email[(at + 1)..]))
            .Select(email => email.ToLowerInvariant()).Distinct(StringComparer.Ordinal)];
    }

    private async Task<Result<SendReceipt>> SubmitAsync(ComposeContext ctx, ComposeDraft draft, Identity identity, long? hold, string html, string text, IOutgoingAddOnSend? addOnSend, CancellationToken ct)
    {
        var roles = await ctx.RolesAsync(false, ct).ConfigureAwait(false);
        if (!roles.IsOk)
        {
            return Result<SendReceipt>.Fail(roles.Error!);
        }

        if (!ReferenceEquals(accounts.SessionFor(draft.AccountId), ctx.Session))
            return Result<SendReceipt>.Fail(new ValidationError("The sending account session changed."));
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
        var accountGuard = AccountGuard(ctx, draft.AccountId);
        var beforeSend = addOnSend is null ? accountGuard : addOnSend.TryIssueAsync;
        var request = JmapRequest.Of([.. Calls(ctx, draft, identity, roles.Value!.Drafts, html, text, Submission("#d", mailFrom), onSuccess)])
            with { BeforeSend = beforeSend };
        var response = await ctx.Client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsOk)
        {
            return Result<SendReceipt>.Fail(response.Error!);
        }

        return await CompleteSubmissionAsync(ctx, draft.AccountId, response.Value!,
            string.Equals(mailFrom, identityMailFrom, StringComparison.OrdinalIgnoreCase) ? null
                : emailId => Submission(emailId, identityMailFrom),
            onSuccess, beforeSend, accountGuard, ct).ConfigureAwait(false);
    }

    private Func<Func<bool>, CancellationToken, Task<bool>> AccountGuard(ComposeContext ctx, string accountKey)
    {
        IReadOnlyList<AccountSessionSnapshot> captures = [new(ctx.Session, null)];
        return (issue, token) => Task.FromResult(!token.IsCancellationRequested &&
            (accounts is IAccountSessionSnapshots snapshots ? snapshots.TryRunWhileCurrent(captures, issue)
                : ReferenceEquals(accounts.SessionFor(accountKey), ctx.Session) && issue()));
    }

    private static async Task<Result<SendReceipt>> CompleteSubmissionAsync(ComposeContext ctx, string accountKey, JmapResponse response,
        Func<string, JsonObject>? identityFallback, JsonObject onSuccess, Func<Func<bool>, CancellationToken, Task<bool>> beforeSend,
        Func<Func<bool>, CancellationToken, Task<bool>> accountGuard, CancellationToken ct)
    {
        var emailArgs = response.Args("e", "Email/set");
        var copy = emailArgs.IsOk ? ReaderCalls.Created(emailArgs.Value!, "d") : Result<string>.Fail(emailArgs.Error!);
        if (!copy.IsOk)
        {
            return Result<SendReceipt>.Fail(SubmissionOps.Plain(copy.Error!));
        }

        var subArgs = response.Args("s", "EmailSubmission/set");
        var created = subArgs.IsOk ? ReaderCalls.Created(subArgs.Value!, "s") : Result<string>.Fail(subArgs.Error!);
        if (created.IsOk)
        {
            return Result<SendReceipt>.Ok(new SendReceipt(accountKey, copy.Value!, created.Value!));
        }

        // a server that only takes the identity's own address as MAIL FROM (RFC 8621 7.5 forbiddenMailFrom; Stalwart: forbiddenFrom): the copy in Drafts is submitted once more with the identity's address (Bulwark sendEmail)
        if (created.Error is SetError e && SubmissionRules.MailFromRefused(e) && identityFallback is not null)
        {
            var again = await ResubmitAsync(ctx, identityFallback(copy.Value!), onSuccess, beforeSend, ct).ConfigureAwait(false);
            if (again.IsOk)
            {
                return Result<SendReceipt>.Ok(new SendReceipt(accountKey, copy.Value!, again.Value!) { EnvelopeRewritten = true });
            }

            created = again;
        }

        // nothing went out: the copy made for the send comes out of Drafts again (the composer keeps the content and saves a fresh draft)
        _ = await SubmissionOps.DestroyEmailAsync(ctx, copy.Value!, ct, accountGuard).ConfigureAwait(false);
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

    private static async Task<Result<string>> ResubmitAsync(ComposeContext ctx, JsonObject submission, JsonObject onSuccess,
        Func<Func<bool>, CancellationToken, Task<bool>> beforeSend, CancellationToken ct)
    {
        var args = new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["create"] = new JsonObject { ["s"] = submission }, ["onSuccessUpdateEmail"] = onSuccess.DeepClone() };
        var request = JmapRequest.Of(JmapRequest.Call("EmailSubmission/set", args, "r")) with { BeforeSend = beforeSend };
        var sent = await ctx.Client.SendAsync(request, ct).ConfigureAwait(false);
        var retry = sent.IsOk ? sent.Value!.Args("r", "EmailSubmission/set") : Result<JsonObject>.Fail(sent.Error!);
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
