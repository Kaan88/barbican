// Mirrors: Rust reader.rs automation_import / automation_find / automation_destroy (the automation-writes feature: import a message into the
// test account, find it, destroy it), RFC 8621 §4.8 (Email/import), §4.6 (Email/set destroy), §4.4 (Email/query); Stalwart
// crates/jmap/src/email/import.rs. M2 plan §6.3 step 1 (R2-14, decision M2-Q2, a proposal pending the user's OK).
// What the C# port changes: Rust named the test account in the source and imported any file it was given. Here the message is synthetic
// (example.org addresses, no real data), it carries a run token in its subject and in its own header, and find and destroy act only on
// messages that carry that token: an id without it is refused and nothing is destroyed. The account check is the caller's
// (ReadOnlyPolicy.TestWriteRefusal, read again for every command); the commands themselves exist only in the AUTOMATION_WRITES build.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Mail.Reader;

/// <summary>The token-marked messages the write-test commands import, find and destroy.</summary>
public static class WriteTestMessages
{
    /// <summary>The header every test message carries with its run token.</summary>
    public const string Header = "X-Barbican-Write-Test";

    /// <summary>Whether a run token is acceptable: 8 to 40 letters and digits.</summary>
    public static bool ValidToken(string? token) =>
        token is { Length: >= 8 and <= 40 } && token.All(char.IsAsciiLetterOrDigit);

    /// <summary>The subject of a test message.</summary>
    public static string Subject(string token) => $"Barbican write test [rx:{token}]";

    /// <summary>A synthetic RFC 5322 message marked with <paramref name="token"/> (CRLF line ends, ASCII only).</summary>
    public static byte[] Build(string token, DateTimeOffset now)
    {
        if (!ValidToken(token))
        {
            throw new ArgumentException("8 to 40 letters and digits", nameof(token));
        }

        var date = now.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture);
        var text = string.Join(
            "\r\n",
            $"Date: {date}",
            "From: Barbican write test <rx@example.org>",
            "To: Barbican write test <rx@example.org>",
            $"Subject: {Subject(token)}",
            $"Message-ID: <rx.{token}@example.org>",
            $"{Header}: {token}",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=us-ascii",
            "Content-Transfer-Encoding: 7bit",
            string.Empty,
            "A synthetic message of a Barbican write test. It is deleted at the end of the run.",
            string.Empty);
        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>Uploads and imports one test message into the Inbox of <paramref name="accountId"/>; the new email id.</summary>
    public static async Task<Result<string>> ImportAsync(JmapClient client, string accountId, string token, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!ValidToken(token))
        {
            return Result<string>.Fail(new ValidationError("The run token must be 8 to 40 letters and digits."));
        }

        var inbox = await ReaderCalls.CallAsync(client, "Mailbox/query", new JsonObject { ["accountId"] = accountId, ["filter"] = new JsonObject { ["role"] = "inbox" } }, ct).ConfigureAwait(false);
        if (!inbox.IsOk)
        {
            return Result<string>.Fail(inbox.Error!);
        }

        if (ReaderCalls.Ids(inbox.Value!) is not [var inboxId, ..])
        {
            return Result<string>.Fail(new ValidationError("The account has no Inbox."));
        }

        var blob = await client.UploadAsync(accountId, Build(token, now), "message/rfc822", ct).ConfigureAwait(false);
        if (!blob.IsOk)
        {
            return Result<string>.Fail(blob.Error!);
        }

        var import = await ReaderCalls.CallAsync(
            client,
            "Email/import",
            new JsonObject
            {
                ["accountId"] = accountId,
                ["emails"] = new JsonObject
                {
                    ["m"] = new JsonObject
                    {
                        ["blobId"] = blob.Value!.BlobId,
                        ["mailboxIds"] = new JsonObject { [inboxId] = true },
                        ["keywords"] = new JsonObject(),
                    },
                },
            },
            ct).ConfigureAwait(false);
        return import.Bind(r => ReaderCalls.Created(r, "m"));
    }

    /// <summary>The ids of the messages that carry <paramref name="token"/> (read-only: Email/query, then Email/get to confirm the mark).</summary>
    public static async Task<Result<IReadOnlyList<string>>> FindAsync(IJmapClient client, string accountId, string token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!ValidToken(token))
        {
            return Result<IReadOnlyList<string>>.Fail(new ValidationError("The run token must be 8 to 40 letters and digits."));
        }

        var query = await ReaderCalls.CallAsync(
            client,
            "Email/query",
            new JsonObject { ["accountId"] = accountId, ["filter"] = new JsonObject { ["subject"] = $"rx:{token}" }, ["limit"] = 50 },
            ct).ConfigureAwait(false);
        if (!query.IsOk)
        {
            return Result<IReadOnlyList<string>>.Fail(query.Error!);
        }

        return await MarkedAsync(client, accountId, token, ReaderCalls.Ids(query.Value!), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Destroys the given test messages: every id must carry <paramref name="token"/> (checked on the server's copy first), else nothing is
    /// destroyed. The number destroyed.
    /// </summary>
    public static async Task<Result<int>> DestroyAsync(IJmapClient client, string accountId, string token, IReadOnlyList<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(ids);
        if (!ValidToken(token))
        {
            return Result<int>.Fail(new ValidationError("The run token must be 8 to 40 letters and digits."));
        }

        if (ids.Count == 0)
        {
            return Result<int>.Ok(0);
        }

        var marked = await MarkedAsync(client, accountId, token, ids, ct).ConfigureAwait(false);
        if (!marked.IsOk)
        {
            return Result<int>.Fail(marked.Error!);
        }

        var foreign = ids.Where(id => !marked.Value!.Contains(id, StringComparer.Ordinal)).ToList();
        if (foreign.Count > 0)
        {
            return Result<int>.Fail(new ValidationError($"refused: {string.Join(" ", foreign)} carry no write-test mark of this run"));
        }

        var set = await ReaderCalls.CallAsync(client, "Email/set", new JsonObject { ["accountId"] = accountId, ["destroy"] = new JsonArray([.. ids.Select(i => (JsonNode)i)]) }, ct).ConfigureAwait(false);
        return set.Map(r => (r["destroyed"] as JsonArray)?.Count ?? 0);
    }

    // The subset of ids whose server copy carries the token in its subject and in the test header.
    private static async Task<Result<IReadOnlyList<string>>> MarkedAsync(IJmapClient client, string accountId, string token, IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Result<IReadOnlyList<string>>.Ok([]);
        }

        var get = await ReaderCalls.CallAsync(
            client,
            "Email/get",
            new JsonObject
            {
                ["accountId"] = accountId,
                ["ids"] = new JsonArray([.. ids.Select(i => (JsonNode)i)]),
                ["properties"] = new JsonArray("id", "subject", "headers"),
            },
            ct).ConfigureAwait(false);
        return get.Map<IReadOnlyList<string>>(r => [.. (r["list"] as JsonArray ?? [])
            .Where(e => string.Equals(e.Str("subject"), Subject(token), StringComparison.Ordinal)
                && e?["headers"] is JsonArray headers
                && string.Equals(ReaderHeaders.Header(headers, Header), token, StringComparison.Ordinal))
            .Select(e => e.Str("id"))
            .OfType<string>()]);
    }
}
