// Mirrors: Bulwark stores/email-store.ts:5601-5648 crossAccountMoveEmails (one client: Email/copy keeps the original with keepOriginal; separate
// logins: Email/get blobId and keywords, download the blob, Email/import it with the original receivedAt), :5728-5773 copyEmailsToAccount (a
// copy leaves the originals), lib/jmap/client.ts:12242 copyEmailAcrossAccounts (keywords carried, no onSuccessDestroyOriginal) and :12290
// importRawEmail; RFC 8621 section 4.4 (Email/get), 4.7 (Email/copy), 4.8 (Email/import); RFC 8620 section 6.2 (download); Stalwart
// crates/jmap/src/email/{copy,import}.rs. spec G-048. Owner actions.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Write;

/// <summary>Copies messages into a folder of another signed-in account, keeping the originals (Bulwark's "Copy to account").</summary>
/// <param name="accountOf">The writer and client of an account key, or null when it is not signed in.</param>
public sealed class CrossAccountCopy(Func<string, (IMailWriter Writer, IJmapClient Client)?> accountOf)
{
    private const string RawType = "message/rfc822";

    /// <summary>
    /// Copies every target into <paramref name="destMailboxId"/> of <paramref name="destAccountKey"/> and reports how many arrived. One client that reaches
    /// both accounts (shared mail) asks the server to copy; two logins move the raw message across with an upload and an Email/import, as Bulwark does.
    /// A message that fails does not stop the rest; the failure names the first error.
    /// </summary>
    public async Task<Result<int>> CopyAsync(IReadOnlyList<MailTarget> targets, string destAccountKey, string destMailboxId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrEmpty(destAccountKey);
        ArgumentException.ThrowIfNullOrEmpty(destMailboxId);
        if (accountOf(destAccountKey) is not var (destWriter, destClient))
        {
            return Result<int>.Fail(new ValidationError("That account is not signed in."));
        }

        var copied = 0;
        var failures = new List<string>();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var failure = await CopyOneAsync(target, destWriter, destClient, destMailboxId, ct).ConfigureAwait(false);
            if (failure is null)
            {
                copied++;
            }
            else
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
        {
            return Result<int>.Fail(new ValidationError(failures.Count == 1
                ? $"Failed to copy email: {failures[0]}"
                : $"Failed to copy {failures.Count} email(s); first error: {failures[0]}"));
        }

        return Result<int>.Ok(copied);
    }

    /// <summary>One message: null when it was copied, else the message to show.</summary>
    private async Task<string?> CopyOneAsync(MailTarget target, IMailWriter destWriter, IJmapClient destClient, string destMailboxId, CancellationToken ct)
    {
        if (accountOf(target.Login) is not var (_, sourceClient))
        {
            return "Source account not connected";
        }

        var fromAccount = target.JmapAccountId(sourceClient.Session.MailAccountId);
        var source = await SourceAsync(sourceClient, fromAccount, target.ServerId, ct).ConfigureAwait(false);
        if (!source.IsOk)
        {
            return source.Error!.Message;
        }

        var (blobId, keywords, receivedAt) = source.Value;
        if (ReferenceEquals(sourceClient, destClient))
        {
            // Delegated/shared folders: one client reaches both accounts, so the server copies the message (Bulwark :5620-5629).
            var copy = await destWriter.CopyToAccountAsync(fromAccount, [new EmailCopyEntry(target.ServerId, [destMailboxId], keywords)], ct).ConfigureAwait(false);
            if (!copy.IsOk)
            {
                return copy.Error!.Message;
            }

            return copy.Value!.Refused.Count > 0 ? copy.Value.Refused[0].Message
                : copy.Value.Created.Count == 0 ? "The server did not return the copy." : null;
        }

        // Separate logins: the blob and the original date travel to the destination, which owns the copy (Bulwark :5631-5645).
        if (string.IsNullOrEmpty(blobId))
        {
            return "Source email has no raw blob to copy";
        }

        var download = await sourceClient.DownloadAsync(fromAccount, blobId, "message.eml", RawType, ct).ConfigureAwait(false);
        if (!download.IsOk)
        {
            return download.Error!.Message;
        }

        using var buffer = new MemoryStream();
        await using (download.Value!.ConfigureAwait(false))
        {
            await download.Value!.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }

        var upload = await destWriter.UploadAsync(buffer.ToArray(), RawType, ct).ConfigureAwait(false);
        if (!upload.IsOk)
        {
            return upload.Error!.Message;
        }

        var import = await destWriter.ImportAsync([new EmailImportEntry(upload.Value!.BlobId, [destMailboxId], keywords, receivedAt)], ct).ConfigureAwait(false);
        if (!import.IsOk)
        {
            return import.Error!.Message;
        }

        return import.Value!.Refused.Count > 0 ? import.Value.Refused[0].Message
            : import.Value.Created.Count == 0 ? "The server did not return the imported message." : null;
    }

    /// <summary>The raw blob, every keyword the message carries and its received date (Email/get), as Bulwark's copy reads them.</summary>
    private static async Task<Result<(string BlobId, IReadOnlyList<string> Keywords, string? ReceivedAt)>> SourceAsync(
        IJmapClient client, string account, string emailId, CancellationToken ct)
    {
        var builder = new JmapRequestBuilder();
        var get = builder.Add("Email/get", new JsonObject
        {
            ["accountId"] = account,
            ["ids"] = new JsonArray(emailId),
            ["properties"] = new JsonArray("blobId", "keywords", "receivedAt"),
        });
        var sent = await client.SendAsync(builder.Build(), ct).ConfigureAwait(false);
        var args = sent.IsOk ? sent.Value!.Args(get.CallId, get.Method) : Result<JsonObject>.Fail(sent.Error!);
        if (!args.IsOk)
        {
            return Result<(string, IReadOnlyList<string>, string?)>.Fail(args.Error!);
        }

        if ((args.Value!.Arr("list") ?? []).OfType<JsonObject>().FirstOrDefault() is not { } email)
        {
            return Result<(string, IReadOnlyList<string>, string?)>.Fail(new ValidationError("The message no longer exists."));
        }

        var keywords = email["keywords"] is JsonObject map
            ? map.Where(p => p.Value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True).Select(p => p.Key).ToList()
            : [];
        return Result<(string, IReadOnlyList<string>, string?)>.Ok(((string?)email["blobId"] ?? string.Empty, keywords, (string?)email["receivedAt"]));
    }
}
