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
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Compose;

/// <summary>Submits a caller-authorized RFC 822 message through its captured account.</summary>
public sealed class RawMessageSender(IAccountManager accounts, TimeProvider time)
{
    private const int MaxBytes = 50 * 1024 * 1024;
    private const int MaxRecipients = 200;

    public Task<Result<RawMessageReceipt>> SendAsync(IAccountSession session, RawMessageRequest request,
        Func<Func<bool>, CancellationToken, Task<bool>> beforeSend, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(beforeSend);
        if (request.EnvelopeRecipients is null) return Task.FromResult(Failure("Invalid raw message or envelope."));
        return SendAsync(new(session, null), request, null,
            (_, guard, issue, token) => beforeSend(() => guard(issue), token), ct);
    }

    /// <summary>Sends a standalone add-on message after verifying the identity and applicable organization policy.</summary>
    public Task<Result<RawMessageReceipt>> SendUnderPolicyAsync(AccountSessionSnapshot captured, RawMessageRequest request,
        OrganizationPolicyService policy, Func<bool, Func<Func<bool>, bool>, Func<bool>, CancellationToken, Task<bool>> authorize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorize);
        if (captured.ActiveSelectionGeneration is null) return Task.FromResult(PolicyDenied());
        return SendAsync(captured, request, policy, authorize, ct);
    }

    private async Task<Result<RawMessageReceipt>> SendAsync(AccountSessionSnapshot captured, RawMessageRequest request,
        OrganizationPolicyService? policy, Func<bool, Func<Func<bool>, bool>, Func<bool>, CancellationToken, Task<bool>> authorize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TrySnapshot(request, out var bytes, out var recipients)) return Failure("Invalid raw message or envelope.");
        var session = captured.Session;
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
        if (!RawMessageHeaders.TryRead(bytes, identity.Email, policy is not null, recipients is null, out var derived))
            return Failure("The raw message sender or envelope is invalid.");
        return await SendVerifiedAsync(ctx, jmapSession, captured, request with { Bytes = bytes, EnvelopeRecipients = recipients ?? derived },
            identity.Email, policy, authorize, ct).ConfigureAwait(false);
    }

    private async Task<Result<RawMessageReceipt>> SendVerifiedAsync(ComposeContext ctx, JmapSession jmapSession, AccountSessionSnapshot captured,
        RawMessageRequest request, string sender, OrganizationPolicyService? policy,
        Func<bool, Func<Func<bool>, bool>, Func<bool>, CancellationToken, Task<bool>> authorize, CancellationToken ct)
    {
        var initial = policy is null ? null : await ReadPolicyAsync(policy, captured, sender, ct).ConfigureAwait(false);
        if (policy is not null && initial is null) return PolicyDenied();
        var bytes = request.Bytes;
        if (initial is { } start)
        {
            var classified = RawClassification.Apply(bytes, start.Labels);
            if (!classified.IsOk) return PolicyDenied();
            bytes = classified.Value!;
            if (!RawMessageHeaders.TryRead(bytes, sender, true, false, out _)) return PolicyDenied();
        }
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
        var policyChanged = false;
        async Task<bool> IssueAsync(bool submission, Func<bool> issue, CancellationToken token)
        {
            var latest = initial;
            if (submission && policy is not null)
                latest = await ReadPolicyAsync(policy, captured, sender, token).ConfigureAwait(false);
            if (initial is { } expected && (latest is not { } current || !SameLabels(expected.Labels, current.Labels)))
            {
                policyChanged = true;
                return false;
            }
            bool IssueWhileAccountCurrent(Func<bool> write) => AccountIssue(captured, jmapSession, write, token);
            bool Guard(Func<bool> write) => initial is { } authorized && latest is { } checkedPolicy
                ? authorized.Snapshot.TryRunWhileCurrent(() => checkedPolicy.Snapshot.TryRunWhileCurrent(() => IssueWhileAccountCurrent(write)))
                : IssueWhileAccountCurrent(write);
            return await authorize(submission, Guard, issue, token).ConfigureAwait(false);
        }
        Task<bool> CleanupFenceAsync(Func<bool> issue, CancellationToken token) =>
            Task.FromResult(AccountIssue(new(captured.Session, null), jmapSession, issue, token));
        Task<bool> ReadbackFenceAsync(Func<bool> issue, CancellationToken token) =>
            authorize(false, write => AccountIssue(captured, jmapSession, write, token), issue, token);
        var client = (JmapClient)ctx.Client;
        var upload = await client.UploadAsync(ctx.Account, bytes, "message/rfc822",
            (issue, token) => IssueAsync(false, issue, token), ct).ConfigureAwait(false);
        if (!upload.IsOk) return Result<RawMessageReceipt>.Fail(upload.Error!);
        var result = await SubmitUploadedAsync(ctx, request, sender, request.EnvelopeRecipients!, hold, roles.Value!, upload.Value!.BlobId,
            (issue, token) => IssueAsync(true, issue, token), CleanupFenceAsync, ReadbackFenceAsync, ct).ConfigureAwait(false);
        return policyChanged && result.Error is not RawMessageOutcomeUnknown ? PolicyDenied() : result;
    }

    private bool AccountIssue(AccountSessionSnapshot captured, JmapSession jmapSession, Func<bool> issue, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || captured.Session.Client is not JmapClient client) return false;
        bool Run() => client.TryRunWhileSessionCurrent(jmapSession, issue);
        return accounts is IAccountSessionSnapshots snapshots
            ? snapshots.TryRunWhileCurrent([captured], Run)
            : ReferenceEquals(accounts.SessionFor(captured.Session.Account.Key), captured.Session) && Run();
    }

    private static async Task<(OrganizationPolicySnapshot Snapshot, ClassificationPolicy Labels)?> ReadPolicyAsync(
        OrganizationPolicyService policy, AccountSessionSnapshot captured, string sender, CancellationToken ct)
    {
        var lookup = await policy.ReadForSessionAsync(captured, ct).ConfigureAwait(false);
        return lookup.Snapshot is { } snapshot && snapshot.ForSender(sender) is { } effective
            ? (snapshot, effective.Labels) : null;
    }

    private static bool SameLabels(ClassificationPolicy first, ClassificationPolicy second) =>
        first.Required == second.Required && string.Equals(first.DefaultId, second.DefaultId, StringComparison.Ordinal) &&
        string.Equals(first.ExtraHeader, second.ExtraHeader, StringComparison.Ordinal) && first.List.SequenceEqual(second.List);

    private static Result<RawMessageReceipt> PolicyDenied() => Result<RawMessageReceipt>.Fail(new RawMessagePolicyDenied());

    private static bool TrySnapshot(RawMessageRequest request, out byte[] bytes, out string[]? recipients)
    {
        bytes = [];
        recipients = null;
        if (request.Bytes is not { Length: > 0 and <= MaxBytes } ||
            string.IsNullOrWhiteSpace(request.LocalAccountKey) || string.IsNullOrWhiteSpace(request.IdentityId)) return false;
        bytes = (byte[])request.Bytes.Clone();
        if (request.EnvelopeRecipients is null) return true;
        recipients = request.EnvelopeRecipients.ToArray();
        return recipients.Length is > 0 and <= MaxRecipients &&
            recipients.All(address => !string.IsNullOrWhiteSpace(address) && RecipientText.IsValidEmail(address) && !address.Any(char.IsControl));
    }

    private async Task<Result<RawMessageReceipt>> SubmitUploadedAsync(ComposeContext ctx, RawMessageRequest request, string fromAddress,
        IReadOnlyList<string> recipients, long? hold, MailboxRoles roles, string blobId,
        Func<Func<bool>, CancellationToken, Task<bool>> fence, Func<Func<bool>, CancellationToken, Task<bool>> accountFence,
        Func<Func<bool>, CancellationToken, Task<bool>> readbackFence, CancellationToken ct)
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
        var receipt = new RawMessageReceipt(request.LocalAccountKey, ctx.Account, request.SubmitOnly ? null : imported.Value!, created.Value!, null)
        {
            Scheduled = hold is not null,
        };
        var createBody = result.Value!.Args("s", "EmailSubmission/set").Value;
        var creation = createBody?["created"]?["raw-submit"] as JsonObject;
        return await ObserveSubmissionAsync(ctx, receipt, creation, recipients, accountFence, readbackFence, ct).ConfigureAwait(false);
    }

    private static async Task<Result<RawMessageReceipt>> ObserveSubmissionAsync(ComposeContext ctx, RawMessageReceipt receipt, JsonObject? creation,
        IReadOnlyList<string> recipients, Func<Func<bool>, CancellationToken, Task<bool>> accountFence,
        Func<Func<bool>, CancellationToken, Task<bool>> readbackFence, CancellationToken ct)
    {
        var initial = creation is null ? null : RawSubmissionReadback.Parse(creation, recipients);
        RawSubmissionObservation? observed = null;
        try { observed = await RawSubmissionReadback.ReadAsync(ctx, receipt.SubmissionId, recipients, readbackFence, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        var readback = RawSubmissionReadback.Merge(initial, observed);
        if (readback is { AllRefused: true })
        {
            if (receipt.EmailId is { } id) await CleanupAsync(ctx, id, accountFence).ConfigureAwait(false);
            return Result<RawMessageReceipt>.Fail(new SetError(receipt.SubmissionId, "recipientsRejected", null, null));
        }
        return Result<RawMessageReceipt>.Ok(receipt with
        {
            ReleaseAt = receipt.Scheduled ? readback?.SendAt : null,
            RejectedRecipients = readback?.RejectedRecipients ?? [],
        });
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
