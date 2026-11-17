// Mirrors: Rust mailimport.rs:278-317 import (batches of the server's maxObjectsInSet clamped to 1..50; skip Message-IDs already in the folder or earlier in the
// same file; refuse a message over maxSizeUpload before uploading; upload then Email/import; report after each batch); RFC 8620 section 6.1 (upload), RFC 8621
// section 4.8 (Email/import: created, notCreated); Bulwark lib/mail-import/{engine,dedupe}.ts, lib/jmap/client.ts importRawEmail :12326; Stalwart
// crates/jmap/src/email/import.rs. Owner unified (M2 wave 2).

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.Import;

/// <summary>The import loop over an <see cref="IImportServer"/>.</summary>
public static class MailImportLoop
{
    /// <summary>The text of a message refused before uploading because it is over maxSizeUpload.</summary>
    public const string TooLarge = "A message is larger than the server takes.";

    /// <summary>Imports the items into a mailbox a batch at a time; <paramref name="progress"/> gets the count of messages handled after each batch.</summary>
    /// <remarks>Cancellation stops between batches (a batch already sent is counted); the outcome so far is returned, never thrown away.</remarks>
    public static async Task<ImportOutcome> ImportAsync(IImportServer server, string mailboxId, IEnumerator<ImportItem> items, Action<int> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(progress);
        var tally = new Tally();
        var batch = Math.Clamp(server.MaxSet, 1, 50);
        var processed = 0;

        // what the folder holds already, so a retried import does not file messages twice
        var have = new HashSet<string>(await server.MessageIdsInAsync(mailboxId, ct).ConfigureAwait(false), StringComparer.Ordinal);
        while (!ct.IsCancellationRequested)
        {
            var chunk = new List<ImportItem>(batch);
            while (chunk.Count < batch && items.MoveNext())
            {
                chunk.Add(items.Current);
            }

            if (chunk.Count == 0)
            {
                break;
            }

            processed += chunk.Count;
            await ImportBatchAsync(server, mailboxId, chunk, have, tally, ct).ConfigureAwait(false);
            progress(processed);
        }

        return new ImportOutcome(tally.Imported, tally.Already, tally.Errors);
    }

    private static async Task ImportBatchAsync(IImportServer server, string mailboxId, List<ImportItem> chunk, HashSet<string> have, Tally tally, CancellationToken ct)
    {
        var entries = new JsonObject();
        for (var i = 0; i < chunk.Count; i++)
        {
            var item = chunk[i];
            if (MboxFormat.MessageId(item.Bytes) is { } id && !have.Add(id))
            {
                tally.Already++;
                continue;
            }

            if (item.Bytes.Length > server.MaxUpload)
            {
                tally.Errors.Add(TooLarge);
                continue;
            }

            var blob = await server.UploadAsync(item.Bytes, ct).ConfigureAwait(false);
            if (blob.IsOk)
            {
                entries[$"m{i}"] = MboxFormat.ImportEntry(blob.Value!, mailboxId, item);
            }
            else
            {
                tally.Errors.Add(blob.Error!.Message);
            }
        }

        if (entries.Count == 0)
        {
            return;
        }

        var count = entries.Count;
        var call = new JsonArray(new JsonArray("Email/import", new JsonObject { ["accountId"] = server.Account, ["emails"] = entries }, "i"));
        var reply = await server.CallAsync(call, ct).ConfigureAwait(false);
        if (reply.IsOk && reply.Value!.Count > 0 && reply.Value[0] is JsonArray { Count: >= 2 } r && r[1] is JsonObject args)
        {
            tally.Imported += (args["created"] as JsonObject)?.Count ?? 0;
            foreach (var (_, error) in args["notCreated"] as JsonObject ?? [])
            {
                tally.Errors.Add(SetErrors.Message(error.Str("type") ?? "error", error.Str("description"), error.Arr("properties") is null ? null : error.Strings("properties")));
            }
        }
        else
        {
            var message = reply.IsOk ? "The server sent an unexpected answer." : reply.Error!.Message;
            tally.Errors.AddRange(Enumerable.Repeat(message, count));
        }
    }

    private sealed class Tally
    {
        public int Imported { get; set; }

        public int Already { get; set; }

        public List<string> Errors { get; } = [];
    }
}
