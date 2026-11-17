// Mirrors: Rust jmap.rs:721 scheduled (EmailSubmission/query undoStatus pending + EmailSubmission/get), :985 reschedule_submission (cancel the original, submit again with HOLDFOR for the new time and the original envelope; the fallbacks of ReschedulePlanner), :1064 cancel_submission,
// schedule.rs:215-292 (banner, action, reschedule); Bulwark lib/jmap/client.ts listScheduledSubmissions, rescheduleEmailSubmission, cancelEmailSubmission, components/email/message-actions.ts;
// RFC 8621 section 7.3 (EmailSubmission/query, filter undoStatus), section 7.5 (undoStatus pending/final/canceled), RFC 4865. Stalwart crates/jmap/src/submission/{query,get,set}.rs.
// Decision D-250: cancelling a scheduled send whose message cannot be moved back to Drafts leaves the copy where it is and says so (Rust destroyed it, which loses the message when no composer holds it).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Compose;

/// <summary>The Scheduled view's data and its actions.</summary>
public sealed class ScheduledSends(IAccountManager accounts, TimeProvider time) : IScheduledSends
{
    public async Task<Result<IReadOnlyList<ScheduledMessage>>> ListAsync(string accountId, CancellationToken ct)
    {
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<IReadOnlyList<ScheduledMessage>>.Fail(ctx.Error!);
        }

        var b = new JmapRequestBuilder();
        var q = b.Add("EmailSubmission/query", new JsonObject { ["accountId"] = ctx.Value!.SubmissionAccount, ["filter"] = new JsonObject { ["undoStatus"] = "pending" } });
        b.Add("EmailSubmission/get", new JsonObject { ["accountId"] = ctx.Value.SubmissionAccount, ["#ids"] = q.Ref("/ids") });
        var r = await ctx.Value.Client.CallAsync(b.Build(), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<ScheduledMessage>>.Fail(r.Error!);
        }

        IReadOnlyList<ScheduledMessage> list =
        [
            .. (r.Value!.MethodResponses[1][1].Arr("list") ?? []).OfType<JsonNode>()
                .Where(s => s.Str("emailId") is not null && s.Str("id") is not null)
                .Select(s => new ScheduledMessage(s.Str("emailId")!, s.Str("id")!, SubmissionOps.SendAt(s) ?? DateTimeOffset.MinValue))
                .OrderBy(m => m.At),
        ];
        return Result<IReadOnlyList<ScheduledMessage>>.Ok(list);
    }

    public async Task<Result<Unit>> RescheduleAsync(string accountId, string submissionId, DateTimeOffset at, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(submissionId);
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var c = ctx.Value!;
        var max = SubmissionOps.MaxDelayedSend(c);
        var checkedHold = DelayedSend.ScheduledHold(at.ToUnixTimeMilliseconds(), time.GetUtcNow().ToUnixTimeMilliseconds(), max);
        if (!checkedHold.IsOk)
        {
            return Result<Unit>.Fail(checkedHold.Error!);
        }

        var pending = await PendingAsync(c, submissionId, ct).ConfigureAwait(false);
        if (!pending.IsOk)
        {
            return Result<Unit>.Fail(pending.Error!);
        }

        var (sub, emailId) = pending.Value;
        var identityId = sub.Str("identityId") ?? "";
        var oldAt = SubmissionOps.SendAt(sub)?.ToUnixTimeMilliseconds();
        var envelope = await SubmissionOps.EnvelopeOfAsync(c, sub, emailId, ct).ConfigureAwait(false);
        if (!envelope.IsOk)
        {
            return Result<Unit>.Fail(envelope.Error!);
        }

        var roles = await c.RolesAsync(false, ct).ConfigureAwait(false);
        var sentId = roles.IsOk ? roles.Value!.Sent : null;
        async Task<Result<string>> CreateAsync(long untilMs) =>
            await SubmissionOps.CreateAsync(c, emailId, identityId, envelope.Value.From, envelope.Value.Recipients, Math.Max(1, (long)Math.Ceiling((untilMs - time.GetUtcNow().ToUnixTimeMilliseconds()) / 1000.0)), sentId, ct).ConfigureAwait(false);

        // cancel the original first: servers may allow one active submission per message
        var cancel = await ReaderCalls.CallAsync(c.Client, "EmailSubmission/set", new JsonObject { ["accountId"] = c.SubmissionAccount, ["update"] = new JsonObject { [submissionId] = new JsonObject { ["undoStatus"] = "canceled" } } }, ct).ConfigureAwait(false);
        if (!cancel.IsOk)
        {
            return Result<Unit>.Fail(cancel.Error!);
        }

        if (cancel.Value!["notUpdated"] is JsonObject bad && bad[submissionId] is { } err)
        {
            return Result<Unit>.Fail(SubmissionOps.Plain(SetErrors.From(submissionId, err)));
        }

        var made = await CreateAsync(at.ToUnixTimeMilliseconds()).ConfigureAwait(false);
        Result<Unit> result = made.IsOk ? Result<Unit>.Ok(default) : await FallBackAsync(c, emailId, oldAt, made.Error!.Message, CreateAsync, ct).ConfigureAwait(false);

        // the canceled record is housekeeping only (counts against the submission limit)
        await SubmissionOps.DestroyRecordAsync(c, submissionId, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<string>> CancelAsync(string accountId, string submissionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(submissionId);
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<string>.Fail(ctx.Error!);
        }

        var cancelled = await SubmissionOps.CancelAsync(ctx.Value!, submissionId, ct).ConfigureAwait(false);
        if (!cancelled.IsOk)
        {
            return Result<string>.Fail(cancelled.Error!);
        }

        if (cancelled.Value is not { } emailId)
        {
            return Result<string>.Fail(new ValidationError("This message is no longer scheduled."));
        }

        return await SubmissionOps.RestoreDraftAsync(ctx.Value!, emailId, ct).ConfigureAwait(false)
            ? Result<string>.Ok(emailId)
            : Result<string>.Fail(new ValidationError("Sending was cancelled, but the message could not be moved to Drafts."));
    }

    /// <summary>The submission, when it is still held (pending), with its message's id.</summary>
    private static async Task<Result<(JsonNode Submission, string EmailId)>> PendingAsync(ComposeContext c, string submissionId, CancellationToken ct)
    {
        var got = await SubmissionOps.GetAsync(c, submissionId, ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<(JsonNode, string)>.Fail(got.Error!);
        }

        if (got.Value is not { } sub)
        {
            return Result<(JsonNode, string)>.Fail(new ValidationError("This message is no longer scheduled."));
        }

        if (!string.Equals(sub.Str("undoStatus"), "pending", StringComparison.Ordinal))
        {
            return Result<(JsonNode, string)>.Fail(new ValidationError(string.Equals(sub.Str("undoStatus"), "final", StringComparison.Ordinal) ? "This message was already sent." : "This message is no longer scheduled."));
        }

        return sub.Str("emailId") is { } emailId
            ? Result<(JsonNode, string)>.Ok((sub, emailId))
            : Result<(JsonNode, string)>.Fail(new ValidationError("This message is no longer scheduled."));
    }

    private static Result<Unit> Fail(string text) => Result<Unit>.Fail(new ValidationError(text));

    /// <summary>The first attempt failed after the original was cancelled: recreate it at its old time while that is still ahead, else put the message back in Drafts, so it never stays in Sent looking sent.</summary>
    private async Task<Result<Unit>> FallBackAsync(ComposeContext c, string emailId, long? oldAt, string why, Func<long, Task<Result<string>>> create, CancellationToken ct)
    {
        var message = $"Reschedule failed and the message is no longer scheduled: {why}";
        foreach (var step in ReschedulePlanner.Fallbacks(oldAt, time.GetUtcNow().ToUnixTimeMilliseconds()))
        {
            switch (step)
            {
                case RecreateAt r when (await create(r.AtMs).ConfigureAwait(false)).IsOk:
                    return Fail($"Nothing was changed: {why}. The message is still scheduled.");
                case RestoreDraft when await SubmissionOps.RestoreDraftAsync(c, emailId, ct).ConfigureAwait(false):
                    message = $"Reschedule failed: {why}. The message is back in Drafts.";
                    break;
                default:
                    break;
            }
        }

        return Fail(message);
    }
}
