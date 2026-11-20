// Mirrors Bulwark lib/jmap/client.ts sendRawEmail/submitRawEmail; RFC 8621 sections 4.8 and 7.5.
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Compose;

/// <summary>Submits a caller-authorized RFC 822 message through its captured account.</summary>
public sealed class RawMessageSender(IAccountManager accounts, TimeProvider time)
{
    private const int MaxBytes = 50 * 1024 * 1024;
    private const int MaxRecipients = 200;

    public async Task<Result<RawMessageReceipt>> SendAsync(IAccountSession session, RawMessageRequest request,
        Func<Func<bool>, CancellationToken, Task<bool>> beforeSend, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(beforeSend);
        if (!TrySnapshot(request, out var bytes, out var recipients)) return Failure("Invalid raw message or envelope.");
        if (!ReferenceEquals(accounts.SessionFor(request.LocalAccountKey), session) || session.Client is not JmapClient client || client.IsReadOnly)
            return Failure("The sending account is unavailable.");
        var resolved = ComposeContext.For(accounts, request.LocalAccountKey);
        if (!resolved.IsOk || !ReferenceEquals(resolved.Value!.Session, session)) return Failure("The sending account changed.");
        var ctx = resolved.Value;
        var jmapSession = client.Session;
        if (!jmapSession.Has(JmapCapabilities.Submission) || jmapSession.Limits.MaxCallsInRequest < 2 ||
            !string.Equals(jmapSession.MailAccountId, ctx.Account, StringComparison.Ordinal) ||
            !string.Equals(jmapSession.AccountFor(JmapCapabilities.Submission), ctx.SubmissionAccount, StringComparison.Ordinal) ||
            !string.Equals(ctx.Account, ctx.SubmissionAccount, StringComparison.Ordinal))
            return Failure("Raw submission is unavailable for this account.");
        var found = await IdentityService.ListCapturedAsync(client, ctx.SubmissionAccount, ct).ConfigureAwait(false);
        if (!found.IsOk) return Result<RawMessageReceipt>.Fail(found.Error!);
        var identity = found.Value!.Select(item => item.Identity).FirstOrDefault(item => string.Equals(item.Id, request.IdentityId, StringComparison.Ordinal));
        if (identity is null || !RecipientText.IsValidEmail(identity.Email)) return Failure("The sending identity is unavailable.");
        if (!RawMessageHeaders.MatchFrom(bytes, identity.Email)) return Failure("The raw message From does not match the sending identity.");
        long? hold = null;
        if (request.ScheduledAt is { } scheduled)
        {
            var duration = DelayedSend.ScheduledHold(scheduled.ToUnixTimeMilliseconds(), time.GetUtcNow().ToUnixTimeMilliseconds(), SubmissionOps.MaxDelayedSend(ctx));
            if (!duration.IsOk) return Result<RawMessageReceipt>.Fail(duration.Error!);
            hold = duration.Value;
        }
        var roles = await ctx.RolesAsync(false, ct).ConfigureAwait(false);
        if (!roles.IsOk) return Result<RawMessageReceipt>.Fail(roles.Error!);
        if (!request.SubmitOnly && roles.Value!.Sent is null) return Failure("No Sent folder.");
        var snapshots = accounts as IAccountSessionSnapshots;
        IReadOnlyList<AccountSessionSnapshot> captured = [new(session, null)];
        bool AccountIssue(Func<bool> issue, CancellationToken token) => !token.IsCancellationRequested &&
            (snapshots is null
                ? ReferenceEquals(accounts.SessionFor(request.LocalAccountKey), session) && client.TryRunWhileSessionCurrent(jmapSession, issue)
                : snapshots.TryRunWhileCurrent(captured, () => client.TryRunWhileSessionCurrent(jmapSession, issue)));
        Task<bool> AccountFenceAsync(Func<bool> issue, CancellationToken token) => Task.FromResult(AccountIssue(issue, token));
        Task<bool> FenceAsync(Func<bool> issue, CancellationToken token) => beforeSend(() => AccountIssue(issue, token), token);
        var upload = await client.UploadAsync(ctx.Account, bytes, "message/rfc822", FenceAsync, ct).ConfigureAwait(false);
        if (!upload.IsOk) return Result<RawMessageReceipt>.Fail(upload.Error!);
        return await SubmitUploadedAsync(ctx, request, identity.Email, recipients, hold, roles.Value!, upload.Value!.BlobId, FenceAsync, AccountFenceAsync, ct).ConfigureAwait(false);
    }

    private static bool TrySnapshot(RawMessageRequest request, out byte[] bytes, out string[] recipients)
    {
        bytes = [];
        recipients = [];
        if (request.Bytes is not { Length: > 0 and <= MaxBytes } || request.EnvelopeRecipients is null ||
            string.IsNullOrWhiteSpace(request.LocalAccountKey) || string.IsNullOrWhiteSpace(request.IdentityId)) return false;
        bytes = (byte[])request.Bytes.Clone();
        recipients = request.EnvelopeRecipients.ToArray();
        return recipients.Length is > 0 and <= MaxRecipients &&
            recipients.All(address => !string.IsNullOrWhiteSpace(address) && RecipientText.IsValidEmail(address) && !address.Any(char.IsControl));
    }

    private async Task<Result<RawMessageReceipt>> SubmitUploadedAsync(ComposeContext ctx, RawMessageRequest request, string fromAddress,
        IReadOnlyList<string> recipients, long? hold, MailboxRoles roles, string blobId,
        Func<Func<bool>, CancellationToken, Task<bool>> fence, Func<Func<bool>, CancellationToken, Task<bool>> accountFence, CancellationToken ct)
    {
        var importArgs = new JsonObject { ["accountId"] = ctx.Account, ["emails"] = new JsonObject { ["raw-import"] = new JsonObject
        {
            ["blobId"] = blobId, ["mailboxIds"] = new JsonObject { [roles.Drafts] = true },
            ["keywords"] = new JsonObject { ["$draft"] = true, ["$seen"] = true },
        } } };
        var submission = BuildSubmission(ctx, request, fromAddress, recipients, hold, roles);
        var issued = false;
        Task<bool> SubmitFenceAsync(Func<bool> issue, CancellationToken token) => fence(() => { var started = issue(); issued |= started; return started; }, token);
        var operation = JmapRequest.Of(JmapRequest.Call("Email/import", importArgs, "i"),
            JmapRequest.Call("EmailSubmission/set", submission, "s")) with { BeforeSend = SubmitFenceAsync, RequireSingleRequest = true };
        Result<JmapResponse> result;
        try { result = await ctx.Client.SendAsync(operation, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (issued) { return Result<RawMessageReceipt>.Fail(new RawMessageOutcomeUnknown()); }
        if (!result.IsOk) return Result<RawMessageReceipt>.Fail(issued ? new RawMessageOutcomeUnknown() : result.Error!);
        var imported = ReadCreated(result.Value!, "i", "Email/import", ctx.Account, "raw-import");
        var created = ReadCreated(result.Value!, "s", "EmailSubmission/set", ctx.SubmissionAccount, "raw-submit");
        if (!imported.IsOk)
        {
            if (imported.Error is RawMessageOutcomeUnknown || created.IsOk || created.Error is RawMessageOutcomeUnknown)
                return Result<RawMessageReceipt>.Fail(new RawMessageOutcomeUnknown());
            return Result<RawMessageReceipt>.Fail(imported.Error!);
        }
        if (!created.IsOk)
        {
            if (created.Error is not RawMessageOutcomeUnknown)
                await CleanupAsync(ctx, imported.Value!, accountFence).ConfigureAwait(false);
            return Result<RawMessageReceipt>.Fail(created.Error!);
        }
        return Result<RawMessageReceipt>.Ok(new(request.LocalAccountKey, ctx.Account, request.SubmitOnly ? null : imported.Value!, created.Value!,
            hold is { } secondsHeld ? time.GetUtcNow().AddSeconds(secondsHeld) : null));
    }

    private static JsonObject BuildSubmission(ComposeContext ctx, RawMessageRequest request, string fromAddress,
        IReadOnlyList<string> recipients, long? hold, MailboxRoles roles)
    {
        var from = new JsonObject { ["email"] = fromAddress };
        if (hold is { } seconds) from["parameters"] = new JsonObject { ["HOLDFOR"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var submission = new JsonObject { ["accountId"] = ctx.SubmissionAccount, ["create"] = new JsonObject { ["raw-submit"] = new JsonObject
        {
            ["emailId"] = "#raw-import", ["identityId"] = request.IdentityId,
            ["envelope"] = new JsonObject { ["mailFrom"] = from, ["rcptTo"] = new JsonArray([.. recipients.Select(recipient => (JsonNode?)new JsonObject { ["email"] = recipient })]) },
        } } };
        if (request.SubmitOnly) submission["onSuccessDestroyEmail"] = new JsonArray("#raw-submit");
        else submission["onSuccessUpdateEmail"] = new JsonObject { ["#raw-submit"] = new JsonObject
        {
            ["mailboxIds/" + roles.Drafts] = null, ["mailboxIds/" + roles.Sent] = true, ["keywords/$draft"] = null,
        } };
        return submission;
    }

    private static Result<string> ReadCreated(JmapResponse response, string callId, string method, string account, string key)
    {
        var rows = response.MethodResponses.Where(row => row.Count > 2 && string.Equals(row[2].AsString(), callId, StringComparison.Ordinal)).ToArray();
        if (rows.Any(row => row.Count != 3)) return Unknown();
        var implicitRows = rows.Where(row => string.Equals(row[0].AsString(), "Email/set", StringComparison.Ordinal)).ToArray();
        if (implicitRows.Length > 0 && (!string.Equals(method, "EmailSubmission/set", StringComparison.Ordinal) || implicitRows.Length != 1 ||
            implicitRows[0][1] is not JsonObject implicitBody || !string.Equals(implicitBody.Str("accountId"), account, StringComparison.Ordinal) ||
            !ValidImplicit(implicitBody))) return Unknown();
        var matches = rows.Except(implicitRows).ToArray();
        if (matches.Length != 1 || matches[0][1] is not JsonObject body) return Unknown();
        var name = matches[0][0].AsString();
        if (string.Equals(name, "error", StringComparison.Ordinal))
        {
            if (implicitRows.Length != 0) return Unknown();
            if (body.Str("type") is not { Length: > 0 } type || string.IsNullOrWhiteSpace(type)) return Unknown();
            var error = MethodErrors.From(method, body);
            return Definite(error) ? Result<string>.Fail(error) : Unknown();
        }
        if (!string.Equals(name, method, StringComparison.Ordinal) || !string.Equals(body.Str("accountId"), account, StringComparison.Ordinal)) return Unknown();
        if ((body["created"] is not null && body["created"] is not JsonObject) ||
            (body["notCreated"] is not null && body["notCreated"] is not JsonObject)) return Unknown();
        var created = body["created"] as JsonObject;
        var refused = body["notCreated"] as JsonObject;
        if ((created?.Count ?? 0) + (refused?.Count ?? 0) != 1) return Unknown();
        if (created is not null && created.Count == 1 && created[key] is JsonObject value && value.Str("id") is { Length: > 0 } id && !string.IsNullOrWhiteSpace(id))
            return Result<string>.Ok(id);
        if (refused is not null && refused.Count == 1 && refused[key] is JsonObject failure &&
            failure.Str("type") is { Length: > 0 } failureType && !string.IsNullOrWhiteSpace(failureType))
            return implicitRows.Length == 0 ? Result<string>.Fail(SetErrors.From(key, failure)) : Unknown();
        return Unknown();
    }

    // RFC 8621 appends an implicit Email/set response using the submission call id.
    // A filing failure does not mean the submission was refused.
    private static bool ValidImplicit(JsonObject body) =>
        MapOrNull(body, "created") && MapOrNull(body, "notCreated") && MapOrNull(body, "updated") &&
        MapOrNull(body, "notUpdated") && ArrayOrNull(body, "destroyed") && MapOrNull(body, "notDestroyed");

    private static bool MapOrNull(JsonObject body, string name) => body[name] is null or JsonObject;
    private static bool ArrayOrNull(JsonObject body, string name) => body[name] is null or JsonArray;

    private static Result<string> Unknown() => Result<string>.Fail(new RawMessageOutcomeUnknown());

    private static bool Definite(JmapError error) => error is MethodError { Type: "invalidArguments" or "forbidden" or "accountNotFound" or "accountNotSupportedByMethod" or "unknownMethod" };

    private static async Task<Result<JsonObject>> CallAsync(ComposeContext ctx, string method, JsonObject args,
        Func<Func<bool>, CancellationToken, Task<bool>> beforeSend, CancellationToken ct)
    {
        var request = JmapRequest.Of(JmapRequest.Call(method, args, "r")) with { BeforeSend = beforeSend };
        var result = await ctx.Client.SendAsync(request, ct).ConfigureAwait(false);
        return result.IsOk ? result.Value!.Args("r", method) : Result<JsonObject>.Fail(result.Error!);
    }

    private static async Task CleanupAsync(ComposeContext ctx, string id, Func<Func<bool>, CancellationToken, Task<bool>> fence)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            _ = await CallAsync(ctx, "Email/set", new JsonObject { ["accountId"] = ctx.Account, ["destroy"] = new JsonArray(id) }, fence, timeout.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cleanup is best-effort and cannot replace the definite submission refusal.
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
#pragma warning restore CA1031
    }

    private static Result<RawMessageReceipt> Failure(string message) => Result<RawMessageReceipt>.Fail(new ValidationError(message));
}
