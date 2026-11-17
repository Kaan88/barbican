// Mirrors: Bulwark lib/mail-export/ (a folder saved as one mbox file, or as one .eml file per message in a folder; oldest first; each message
// downloaded as its blob, nothing changed on the server), RFC 8621 section 4.4 (Email/query inMailbox, sort receivedAt) and RFC 8620 section 6.2
// (download). spec G-046. Owner unified. Read-only: an export never writes to the account, so it is allowed on read-only accounts too.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Import;

/// <summary>Saves a folder's messages as mbox or as .eml files.</summary>
/// <param name="clientOf">The client of an account key, or null when it is not signed in.</param>
public sealed class MailExportService(Func<string, IJmapClient?> clientOf)
{
    private const int Page = 256;

    /// <summary>Writes the folder's messages, oldest first, to one mbox stream; reports the count written. Returns the count.</summary>
    public Task<Result<int>> ExportMboxAsync(MailboxRef folder, Stream output, IProgress<int>? progress, CancellationToken ct) =>
        RunAsync(folder, progress, (raw, email, c) => MboxWriter.AppendAsync(output, raw, Sender(email), Received(email), Keywords(email), c), ct);

    /// <summary>Writes each message of the folder to its own .eml file in <paramref name="directory"/> (named by subject, made unique).</summary>
    public Task<Result<int>> ExportEmlAsync(MailboxRef folder, string directory, IProgress<int>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        return RunAsync(folder, progress, async (raw, email, c) =>
        {
            var file = new FileStream(UniquePath(directory, (string?)email["subject"]), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (file.ConfigureAwait(false))
            {
                await file.WriteAsync(raw, c).ConfigureAwait(false);
            }
        }, ct);
    }

    private async Task<Result<int>> RunAsync(MailboxRef folder, IProgress<int>? progress, Func<ReadOnlyMemory<byte>, JsonObject, CancellationToken, Task> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (clientOf(folder.AccountKey) is not { } client)
        {
            return Result<int>.Fail(new Jmap.Errors.ValidationError("That account is not signed in."));
        }

        var account = string.IsNullOrEmpty(folder.AccountId) ? client.Session.MailAccountId : folder.AccountId;
        var written = 0;
        for (var position = 0; ; position += Page)
        {
            var page = await PageAsync(client, account, folder.MailboxId, position, ct).ConfigureAwait(false);
            if (!page.IsOk)
            {
                return Result<int>.Fail(page.Error!);
            }

            foreach (var email in page.Value!.Emails)
            {
                ct.ThrowIfCancellationRequested();
                var raw = await DownloadAsync(client, account, email, ct).ConfigureAwait(false);
                if (!raw.IsOk)
                {
                    return Result<int>.Fail(raw.Error!);
                }

                await write(raw.Value!, email, ct).ConfigureAwait(false);
                written++;
                progress?.Report(written);
            }

            if (position + Page >= page.Value.Total)
            {
                return Result<int>.Ok(written);
            }
        }
    }

    private static async Task<Result<(IReadOnlyList<JsonObject> Emails, int Total)>> PageAsync(IJmapClient client, string account, string mailboxId, int position, CancellationToken ct)
    {
        var b = new Jmap.Requests.JmapRequestBuilder();
        var q = b.Add("Email/query", new JsonObject
        {
            ["accountId"] = account,
            ["filter"] = new JsonObject { ["inMailbox"] = mailboxId },
            ["sort"] = new JsonArray(new JsonObject { ["property"] = "receivedAt", ["isAscending"] = true }),
            ["position"] = position,
            ["limit"] = Page,
            ["calculateTotal"] = true,
        });
        var g = b.Add("Email/get", new JsonObject
        {
            ["accountId"] = account,
            ["#ids"] = q.Ref("/ids"),
            ["properties"] = new JsonArray("id", "blobId", "subject", "from", "receivedAt", "keywords"),
        });
        var sent = await client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<(IReadOnlyList<JsonObject>, int)>.Fail(sent.Error!);
        }

        var query = sent.Value!.Args(q.CallId, q.Method);
        var got = sent.Value.Args(g.CallId, g.Method);
        if (!query.IsOk || !got.IsOk)
        {
            return Result<(IReadOnlyList<JsonObject>, int)>.Fail((query.Error ?? got.Error)!);
        }

        var total = (int?)query.Value!["total"] ?? 0;
        return Result<(IReadOnlyList<JsonObject>, int)>.Ok(([.. (got.Value!.Arr("list") ?? []).OfType<JsonObject>()], total));
    }

    private static async Task<Result<ReadOnlyMemory<byte>>> DownloadAsync(IJmapClient client, string account, JsonObject email, CancellationToken ct)
    {
        var d = await client.DownloadAsync(account, (string?)email["blobId"] ?? "", "message.eml", "message/rfc822", ct).ConfigureAwait(false);
        if (!d.IsOk)
        {
            return Result<ReadOnlyMemory<byte>>.Fail(d.Error!);
        }

        using var buffer = new MemoryStream();
        await using (d.Value!.ConfigureAwait(false))
        {
            await d.Value!.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }

        return Result<ReadOnlyMemory<byte>>.Ok(buffer.ToArray());
    }

    private static string? Sender(JsonObject email) => (string?)email["from"]?[0]?["email"];

    private static DateTimeOffset Received(JsonObject email) =>
        DateTimeOffset.TryParse((string?)email["receivedAt"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.UnixEpoch;

    private static List<string> Keywords(JsonObject email) =>
        email["keywords"] is JsonObject k ? [.. k.Where(p => p.Value is JsonValue v && v.TryGetValue<bool>(out var on) && on).Select(p => p.Key)] : [];

    private static string UniquePath(string directory, string? subject)
    {
        var bad = Path.GetInvalidFileNameChars();
        var stem = new string([.. (string.IsNullOrWhiteSpace(subject) ? "message" : subject.Trim()).Select(c => bad.Contains(c) ? '_' : c)]);
        stem = stem.Length > 80 ? stem[..80] : stem;
        var path = Path.Combine(directory, stem + ".eml");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(directory, $"{stem} ({n}).eml");
        }

        return path;
    }
}
