// Mirrors: Rust jmap.rs:1064 cancel_submission, :1055 restore_draft, :985 reschedule_submission (create closure), :712 max_delayed_send; Bulwark lib/jmap/client.ts cancelEmailSubmission, rescheduleEmailSubmission, sendEmail (holdFor);
// RFC 8621 section 7.5 (EmailSubmission/set: undoStatus canceled, onSuccessUpdateEmail), RFC 4865 (FUTURERELEASE, HOLDFOR). Stalwart crates/jmap/src/submission/set.rs (undoStatus update; a pending submission cancels; a final one answers cannotUnsend).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Compose;

/// <summary>The EmailSubmission calls the sender, the undo and the Scheduled view share.</summary>
internal static class SubmissionOps
{
    /// <summary>The longest hold (maxDelayedSend with FUTURERELEASE, Stalwart's 30 days clamped to 7); 0 when the server cannot delay.</summary>
    public static long MaxDelayedSend(ComposeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var element = ctx.Client.Session.CapabilityObject(ctx.SubmissionAccount, JmapCapabilities.Submission);
        var node = element is { } e ? JsonNode.Parse(e.GetRawText()) as JsonObject : null;
        return node is null ? 0 : DelayedSend.Limit(node, ctx.Client.Session.IsStalwart);
    }

    /// <summary>A SetError as the user reads it: the words only, without the id of the object it was about.</summary>
    public static JmapError Plain(JmapError error) =>
        error is SetError e ? new ValidationError(SetErrors.Message(e.Type, e.Description, e.Properties)) : error;

    /// <summary>One submission as the server holds it, or null when it is gone.</summary>
    public static async Task<Result<JsonNode?>> GetAsync(ComposeContext ctx, string submissionId, CancellationToken ct)
    {
        var r = await ReaderCalls.CallAsync(
            ctx.Client,
            "EmailSubmission/get",
            new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["ids"] = new JsonArray(submissionId), ["properties"] = new JsonArray("emailId", "identityId", "envelope", "sendAt", "undoStatus") },
            ct).ConfigureAwait(false);
        return r.IsOk ? Result<JsonNode?>.Ok((r.Value!["list"] as JsonArray)?.FirstOrDefault()) : Result<JsonNode?>.Fail(r.Error!);
    }

    /// <summary>
    /// undoStatus canceled (and the check that it took: a message already released says so), then the canceled record is destroyed (housekeeping: it only counts against the submission limit).
    /// Returns the message's id.
    /// </summary>
    public static async Task<Result<string?>> CancelAsync(ComposeContext ctx, string submissionId, CancellationToken ct)
    {
        var request = JmapRequest.Of(
            JmapRequest.Call("EmailSubmission/set", new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["update"] = new JsonObject { [submissionId] = new JsonObject { ["undoStatus"] = "canceled" } } }, "c"),
            JmapRequest.Call("EmailSubmission/get", new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["ids"] = new JsonArray(submissionId), ["properties"] = new JsonArray("emailId", "undoStatus") }, "g"));
        var sent = await ctx.Client.SendAsync(request, ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<string?>.Fail(sent.Error!);
        }

        var set = sent.Value!.Args("c", "EmailSubmission/set");
        if (!set.IsOk)
        {
            return Result<string?>.Fail(set.Error!);
        }

        if (set.Value!["notUpdated"] is JsonObject bad && bad[submissionId] is { } err)
        {
            return Result<string?>.Fail(Plain(SetErrors.From(submissionId, err)));
        }

        var got = sent.Value.Args("g", "EmailSubmission/get");
        var sub = got.IsOk ? (got.Value!["list"] as JsonArray)?.FirstOrDefault() : null;
        if (sub.Str("undoStatus") is { } status && !string.Equals(status, "canceled", StringComparison.Ordinal))
        {
            return Result<string?>.Fail(new ValidationError("The message has already been sent."));
        }

        await DestroyRecordAsync(ctx, submissionId, ct).ConfigureAwait(false);
        return Result<string?>.Ok(sub.Str("emailId"));
    }

    public static async Task DestroyRecordAsync(ComposeContext ctx, string submissionId, CancellationToken ct) =>
        _ = await ReaderCalls.CallAsync(ctx.Client, "EmailSubmission/set", new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["destroy"] = new JsonArray(submissionId) }, ct).ConfigureAwait(false);

    /// <summary>The message back into Drafts with <c>$draft</c> (retried once with fresh folder roles); false when it could not.</summary>
    public static async Task<bool> RestoreDraftAsync(ComposeContext ctx, string emailId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var roles = await ctx.RolesAsync(attempt > 0, ct).ConfigureAwait(false);
            if (!roles.IsOk)
            {
                continue;
            }

            var patch = new JsonObject { ["mailboxIds"] = new JsonObject { [roles.Value!.Drafts] = true }, ["keywords/$draft"] = true };
            var r = await ReaderCalls.CallAsync(ctx.Client, "Email/set", new JsonObject { ["accountId"] = ctx.Account, ["update"] = new JsonObject { [emailId] = patch } }, ct).ConfigureAwait(false);
            if (r.IsOk && SetErrors.Read(r.Value).Failed.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A submission of an existing message (reschedule, send now): the envelope with HOLDFOR when <paramref name="holdSeconds"/> is set, and the message filed in Sent with <c>$draft</c> dropped when the server finishes it.
    /// Returns the new submission's id.
    /// </summary>
    public static async Task<Result<string>> CreateAsync(ComposeContext ctx, string emailId, string identityId, string mailFrom, IReadOnlyList<string> recipients, long? holdSeconds, string? sentId, CancellationToken ct)
    {
        var from = new JsonObject { ["email"] = mailFrom };
        if (holdSeconds is { } h)
        {
            from["parameters"] = new JsonObject { ["HOLDFOR"] = h.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        var create = new JsonObject
        {
            ["emailId"] = emailId,
            ["identityId"] = identityId,
            ["envelope"] = new JsonObject { ["mailFrom"] = from, ["rcptTo"] = new JsonArray([.. recipients.Select(r => (JsonNode?)new JsonObject { ["email"] = r })]) },
        };
        var args = new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["create"] = new JsonObject { ["n"] = create } };
        if (sentId is not null)
        {
            args["onSuccessUpdateEmail"] = new JsonObject { ["#n"] = new JsonObject { ["mailboxIds"] = new JsonObject { [sentId] = true }, ["keywords/$draft"] = null } };
        }

        var r = await ReaderCalls.CallAsync(ctx.Client, "EmailSubmission/set", args, ct).ConfigureAwait(false);
        return r.IsOk ? ReaderCalls.Created(r.Value!, "n") : Result<string>.Fail(r.Error!);
    }

    /// <summary>The recipients and sender of an existing submission: its envelope, else the message's own fields (Rust reschedule_submission).</summary>
    public static async Task<Result<(string From, IReadOnlyList<string> Recipients)>> EnvelopeOfAsync(ComposeContext ctx, JsonNode submission, string emailId, CancellationToken ct)
    {
        if (submission["envelope"]?["rcptTo"] is JsonArray { Count: > 0 } rcpt)
        {
            var from = submission["envelope"]!["mailFrom"].Str("email") ?? "";
            return Result<(string, IReadOnlyList<string>)>.Ok((from.Length == 0 ? ctx.Username : from, [.. rcpt.Select(x => x.Str("email")).OfType<string>()]));
        }

        var e = await ReaderCalls.CallAsync(ctx.Client, "Email/get", new JsonObject { ["accountId"] = ctx.Account, ["ids"] = new JsonArray(emailId), ["properties"] = new JsonArray("from", "to", "cc", "bcc") }, ct).ConfigureAwait(false);
        if (!e.IsOk)
        {
            return Result<(string, IReadOnlyList<string>)>.Fail(e.Error!);
        }

        var m = (e.Value!["list"] as JsonArray)?.FirstOrDefault();
        IReadOnlyList<string> rc = [.. RecipientKeys.SelectMany(k => (m?[k] as JsonArray ?? []).Select(x => x.Str("email"))).OfType<string>()];
        var fromEmail = (m?["from"] as JsonArray is { Count: > 0 } fa ? fa[0] : null).Str("email");
        return Result<(string, IReadOnlyList<string>)>.Ok((string.IsNullOrEmpty(fromEmail) ? ctx.Username : fromEmail, rc));
    }

    private static readonly string[] RecipientKeys = ["to", "cc", "bcc"];

    public static DateTimeOffset? SendAt(JsonNode? submission) =>
        submission.Str("sendAt") is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at : null;
}
