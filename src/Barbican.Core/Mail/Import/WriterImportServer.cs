// Mirrors: Rust mailimport.rs:240 Server for the live client (account, max_set, max_upload, upload, call, message_ids_in); RFC 8620 §6.1
// (upload), RFC 8621 §4.8 (Email/import: created and notCreated by creation id). M2 plan 6.5: uploads and imports go through the
// actions' IMailWriter. Owner unified.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Write;

namespace Barbican.Core.Mail.Import;

/// <summary>The import loop's server over an account's writer (writes) and client (limits, the folder's Message-IDs).</summary>
public sealed class WriterImportServer(IMailWriter writer, IJmapClient client) : IImportServer
{
    /// <inheritdoc />
    public string Account => client.Session.MailAccountId;

    /// <inheritdoc />
    public int MaxSet => client.Session.Limits.MaxObjectsInSet;

    /// <inheritdoc />
    public int MaxUpload => (int)Math.Min(int.MaxValue, client.Session.Limits.MaxSizeUpload);

    /// <inheritdoc />
    public async Task<Result<string>> UploadAsync(byte[] data, CancellationToken ct)
    {
        var r = await writer.UploadAsync(data, "message/rfc822", ct).ConfigureAwait(false);
        return r.IsOk ? Result<string>.Ok(r.Value!.BlobId) : Result<string>.Fail(r.Error!);
    }

    /// <inheritdoc />
    /// <remarks>Only the loop's one Email/import call is understood; it goes out through <see cref="IMailWriter.ImportAsync"/>.</remarks>
    public async Task<Result<IReadOnlyList<JsonNode>>> CallAsync(JsonNode calls, CancellationToken ct)
    {
        if (calls is not JsonArray { Count: 1 } a || a[0] is not JsonArray { Count: 3 } call || call[1]?["emails"] is not JsonObject emails)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(new ValidationError("Only Email/import goes through the import server."));
        }

        var ids = emails.Select(kv => kv.Key).ToList();
        var entries = emails.Select(kv => Entry(kv.Value)).ToList();
        var r = await writer.ImportAsync(entries, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(r.Error!);
        }

        var created = new JsonObject();
        foreach (var (i, id) in r.Value!.Created)
        {
            created[ids[i]] = new JsonObject { ["id"] = id };
        }

        var notCreated = new JsonObject();
        foreach (var e in r.Value.Refused)
        {
            // the writer names a refused entry by its creation id "e<index>"
            var key = e.Id.Length > 1 && int.TryParse(e.Id.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out var i) && i >= 0 && i < ids.Count ? ids[i] : e.Id;
            notCreated[key] = new JsonObject { ["type"] = e.Type, ["description"] = e.Description };
        }

        return Result<IReadOnlyList<JsonNode>>.Ok([new JsonArray("Email/import", new JsonObject { ["created"] = created, ["notCreated"] = notCreated }, "i")]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> MessageIdsInAsync(string mailboxId, CancellationToken ct)
    {
        var b = new JmapRequestBuilder();
        var q = b.Add("Email/query", new JsonObject { ["accountId"] = Account, ["filter"] = new JsonObject { ["inMailbox"] = mailboxId } });
        var sent = await client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        var ids = sent.IsOk && sent.Value!.Args(q.CallId, q.Method) is { IsOk: true } qa ? qa.Value!.Strings("ids") : [];
        var have = new HashSet<string>(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return have;
        }

        var got = await client.GetAllAsync("Email/get", Account, ids, new JsonArray("id", "messageId"), ct).ConfigureAwait(false);
        foreach (var m in got.IsOk ? got.Value! : [])
        {
            foreach (var id in m.Arr("messageId") is null ? [] : m.Strings("messageId"))
            {
                have.Add(id);
            }
        }

        return have;
    }

    private static EmailImportEntry Entry(JsonNode? e) => new(
        e.Str("blobId") ?? "",
        [.. (e?["mailboxIds"] as JsonObject ?? []).Select(kv => kv.Key)],
        [.. (e?["keywords"] as JsonObject ?? []).Select(kv => kv.Key)],
        e.Str("receivedAt"));
}
