// Mirrors: Rust mdn.rs:202 send (upload, Email/import into Sent with $seen, EmailSubmission/set with an explicit envelope, destroy the imported copy
// when the submission fails, then $MDNSent; a failed flag write is remembered for the run so the message does not ask again), :178 SENT;
// RFC 8098, RFC 8621 §4.8 (Email/import), §7.5 (EmailSubmission/set envelope); Stalwart 0.16.27 has no MDN/send (J-068 n/a), so this is the
// path; Bulwark lib/jmap/client.ts sendMdn (RFC 9007 MDN/send there). Never on a read-only account. Owner reader.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Mail.Mdn;

/// <summary>Sends read receipts through the account (upload, import into Sent, submission).</summary>
public sealed class MdnSender(TimeProvider time)
{
    private const string Submission = "urn:ietf:params:jmap:submission";

    private readonly Lock _lock = new();
    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);

    /// <summary>Uploads raw bytes into an account and returns the blob id.</summary>
    public delegate Task<Result<string>> Uploader(string accountId, ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>The uploader of a real client, or null for a client that cannot upload.</summary>
    public static Uploader? UploaderFor(IJmapClient client) => client is JmapClient c
        ? async (account, data, ct) => (await c.UploadAsync(account, data, "message/rfc822", ct).ConfigureAwait(false)).Map(b => b.BlobId)
        : null;

    /// <summary>Whether a receipt for this message went out in this run (when $MDNSent could not be written).</summary>
    public bool SentThisRun(string emailId)
    {
        lock (_lock)
        {
            return _sent.Contains(emailId);
        }
    }

    /// <summary>
    /// Sends the receipt for <paramref name="message"/> (its id, headers, messageId, subject, to, cc) and flags it $MDNSent. A failure names the step;
    /// when the submission fails, the copy imported into Sent is destroyed again.
    /// </summary>
    public async Task<Result<Unit>> SendAsync(IJmapClient client, Uploader upload, JsonNode message, bool automatic, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(message);
        if (client.IsReadOnly)
        {
            return Fail("Receipts are never sent from a read-only account.");
        }

        if (MdnMessage.RequestedBy(message["headers"] as JsonArray ?? []) is not { } to)
        {
            return Fail("The message asks for no receipt.");
        }

        var account = client.Session.MailAccountId;
        var submissionAccount = client.Session.AccountFor(Submission) is { Length: > 0 } sa ? sa : account;
        var picked = await OptionsAsync(client, submissionAccount, message, to, automatic, ct).ConfigureAwait(false);
        if (!picked.IsOk)
        {
            return Result<Unit>.Fail(picked.Error!);
        }

        var (options, identityId) = picked.Value;
        var now = time.GetUtcNow();
        var raw = MdnMessage.Build(options, now, MdnMessage.RandomToken(now));
        var imported = await ImportAsync(client, upload, account, raw, ct).ConfigureAwait(false);
        if (!imported.IsOk)
        {
            return Result<Unit>.Fail(imported.Error!);
        }

        var submitted = await SubmitAsync(client, submissionAccount, imported.Value!, identityId, options, ct).ConfigureAwait(false);
        if (!submitted.IsOk)
        {
            // nothing went out: take the receipt back out of Sent, where it would pass for a sent one
            await ReaderCalls.CallAsync(client, "Email/set", new JsonObject { ["accountId"] = account, ["destroy"] = new JsonArray(imported.Value!) }, ct).ConfigureAwait(false);
            return Fail($"Not sent: {submitted.Error!.Message}");
        }

        // the receipt is out; a failed flag write must not read as a failed send, and the run remembers it
        var id = message.Str("id") ?? string.Empty;
        lock (_lock)
        {
            _sent.Add(id);
        }

        await ReaderCalls.CallAsync(
            client,
            "Email/set",
            new JsonObject { ["accountId"] = account, ["update"] = new JsonObject { [id] = new JsonObject { ["keywords/$mdnsent"] = true } } },
            ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }

    // The identity the message was addressed to and the receipt's fields.
    private static async Task<Result<(MdnOptions Options, string IdentityId)>> OptionsAsync(IJmapClient client, string account, JsonNode message, string to, bool automatic, CancellationToken ct)
    {
        var identities = await ReaderCalls.CallAsync(client, "Identity/get", new JsonObject { ["accountId"] = account, ["ids"] = null }, ct).ConfigureAwait(false);
        if (!identities.IsOk)
        {
            return Result<(MdnOptions, string)>.Fail(identities.Error!);
        }

        IReadOnlyList<JsonNode> list = [.. (identities.Value!["list"] as JsonArray ?? []).OfType<JsonNode>()];
        if (MdnMessage.PickIdentity(list, message) is not { } identity)
        {
            return Result<(MdnOptions, string)>.Fail(new ValidationError("No identity to send the receipt from."));
        }

        var options = new MdnOptions
        {
            To = to,
            FromEmail = identity.Str("email") ?? string.Empty,
            FromName = identity.Str("name") ?? string.Empty,
            OriginalMessageId = message["messageId"]?[0].AsString() ?? string.Empty,
            OriginalSubject = message.Str("subject") ?? string.Empty,
            OriginalRecipient = identity.Str("email") ?? string.Empty,
            Automatic = automatic,
        };
        return Result<(MdnOptions, string)>.Ok((options, identity.Str("id") ?? string.Empty));
    }

    private static Result<Unit> Fail(string text) => Result<Unit>.Fail(new ValidationError(text));

    private static async Task<Result<string>> ImportAsync(IJmapClient client, Uploader upload, string account, string raw, CancellationToken ct)
    {
        // Mailbox/get with the role read here (one small call; a role filter on Mailbox/query is not answered by every server alike)
        var boxes = await ReaderCalls.CallAsync(client, "Mailbox/get", new JsonObject { ["accountId"] = account, ["ids"] = null, ["properties"] = new JsonArray("id", "role") }, ct).ConfigureAwait(false);
        if (!boxes.IsOk)
        {
            return Result<string>.Fail(boxes.Error!);
        }

        if ((boxes.Value!["list"] as JsonArray ?? []).FirstOrDefault(b => string.Equals(b.Str("role"), "sent", StringComparison.Ordinal)).Str("id") is not { } sentId)
        {
            return Result<string>.Fail(new ValidationError("No Sent folder."));
        }

        var blob = await upload(account, Encoding.UTF8.GetBytes(raw), ct).ConfigureAwait(false);
        if (!blob.IsOk)
        {
            return Result<string>.Fail(blob.Error!);
        }

        var import = await ReaderCalls.CallAsync(
            client,
            "Email/import",
            new JsonObject
            {
                ["accountId"] = account,
                ["emails"] = new JsonObject
                {
                    ["m"] = new JsonObject { ["blobId"] = blob.Value, ["mailboxIds"] = new JsonObject { [sentId] = true }, ["keywords"] = new JsonObject { ["$seen"] = true } },
                },
            },
            ct).ConfigureAwait(false);
        return import.Bind(r => ReaderCalls.Created(r, "m"));
    }

    private static async Task<Result<string>> SubmitAsync(IJmapClient client, string account, string emailId, string identityId, MdnOptions options, CancellationToken ct)
    {
        var r = await ReaderCalls.CallAsync(
            client,
            "EmailSubmission/set",
            new JsonObject
            {
                ["accountId"] = account,
                ["create"] = new JsonObject
                {
                    ["r"] = new JsonObject
                    {
                        ["emailId"] = emailId,
                        ["identityId"] = identityId,
                        ["envelope"] = new JsonObject
                        {
                            ["mailFrom"] = new JsonObject { ["email"] = MdnMessage.HeaderAtom(options.FromEmail) },
                            ["rcptTo"] = new JsonArray(new JsonObject { ["email"] = MdnMessage.HeaderAtom(options.To) }),
                        },
                    },
                },
            },
            ct).ConfigureAwait(false);
        return r.Bind(x => ReaderCalls.Created(x, "r"));
    }
}
