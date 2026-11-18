// See IContactPhotos. Mirrors Rust contacts_x.rs:540 save (photo edits), :430 photo_bytes; the upload goes through the same Blob/upload the mail writer uses.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Write;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactPhotos"/>.</summary>
public sealed class ContactPhotos(IAccountManager accounts, IContactStore store, IContactWriter writer) : IContactPhotos
{
    public async Task<Result<Unit>> SetAsync(string accountKey, string cardId, byte[] image, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(cardId);
        ArgumentNullException.ThrowIfNull(image);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var card = store.Snapshot(accountKey)?.Cards.FirstOrDefault(c => string.Equals(c.Str("id"), cardId, StringComparison.Ordinal));
        if (card is null)
        {
            return Result<Unit>.Fail(new ValidationError("This contact is no longer in the list."));
        }

        var shrunk = ContactPhoto.Shrink(image);
        if (!shrunk.IsOk)
        {
            return Result<Unit>.Fail(shrunk.Error!);
        }

        var key = PhotoKey(card) ?? FreeKey(card);
        var account = card.Str("_acc") ?? client.Value!.Session.AccountFor(ContactOptions.Capability);
        var uploaded = await new MailWriter(client.Value!, account).UploadAsync(shrunk.Value!.Bytes, shrunk.Value.MediaType, ct).ConfigureAwait(false);
        if (uploaded.IsOk)
        {
            var blob = new JsonObject { ["@type"] = "Media", ["kind"] = "photo", ["blobId"] = uploaded.Value!.BlobId, ["mediaType"] = shrunk.Value.MediaType };
            var done = await writer.UpdateAsync(accountKey, cardId, new JsonObject { ["media/" + ContactPatches.PointerSegment(key)] = blob }, ct).ConfigureAwait(false);
            if (done.IsOk || !MediaRefused(done.Error))
            {
                return done;
            }
        }

        // Stalwart 0.16 refuses a blobId in media, and an upload may fail: the picture goes inline
        var inline = new JsonObject
        {
            ["@type"] = "Media",
            ["kind"] = "photo",
            ["uri"] = $"data:{shrunk.Value.MediaType};base64,{Convert.ToBase64String(shrunk.Value.Bytes)}",
            ["mediaType"] = shrunk.Value.MediaType,
        };
        return await writer.UpdateAsync(accountKey, cardId, new JsonObject { ["media/" + ContactPatches.PointerSegment(key)] = inline }, ct).ConfigureAwait(false);
    }

    public async Task<Result<Unit>> RemoveAsync(string accountKey, string cardId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(cardId);
        var card = store.Snapshot(accountKey)?.Cards.FirstOrDefault(c => string.Equals(c.Str("id"), cardId, StringComparison.Ordinal));
        return PhotoKey(card) is { } key
            ? await writer.UpdateAsync(accountKey, cardId, new JsonObject { ["media/" + ContactPatches.PointerSegment(key)] = null }, ct).ConfigureAwait(false)
            : Result<Unit>.Ok(default);
    }

    public async Task<Result<(string MediaType, byte[] Bytes)>> BytesAsync(string accountKey, JsonNode card, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(card);
        if (PhotoKey(card) is not { } key || ContactJson.Get(ContactJson.Get(card, "media"), key) is not { } photo)
        {
            return Result<(string, byte[])>.Fail(new ValidationError("This contact has no photo."));
        }

        if (photo.Str("uri") is { } uri)
        {
            return uri.StartsWith("data:", StringComparison.Ordinal)
                ? ContactPhoto.FromDataUri(uri)
                : Result<(string, byte[])>.Fail(new ValidationError("This photo is a link and is not loaded."));
        }

        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk || photo.Str("blobId") is not { } blob)
        {
            return Result<(string, byte[])>.Fail(client.Error ?? new ValidationError("This photo has no picture."));
        }

        var type = photo.Str("mediaType") ?? "image/jpeg";
        var account = card.Str("_acc") ?? client.Value!.Session.AccountFor(ContactOptions.Capability);
        var stream = await client.Value!.DownloadAsync(account, blob, "photo", type, ct).ConfigureAwait(false);
        if (!stream.IsOk)
        {
            return Result<(string, byte[])>.Fail(stream.Error!);
        }

        await using (stream.Value!.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.Value!.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                // a server that sends more than a photo may hold is cut off, not read to its end
                if (buffer.Length + read > ContactPhoto.MaxInputBytes)
                {
                    return Result<(string, byte[])>.Fail(new ValidationError("This picture is too large (the limit is 8 MB)."));
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            return Result<(string, byte[])>.Ok((type, buffer.ToArray()));
        }
    }

    public async Task<Result<IReadOnlyList<JsonNode>>> InlineForExportAsync(string accountKey, IReadOnlyList<JsonNode> cards, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(cards);
        var output = new List<JsonNode>();
        foreach (var source in cards)
        {
            var card = source.DeepClone();
            if (ContactJson.Get(card, "media") is JsonObject media)
            {
                foreach (var (k, m) in media.Where(kv => kv.Value is JsonObject o && o["uri"] is null && o["blobId"] is not null).ToList())
                {
                    var asPhoto = (JsonObject)m!.DeepClone();
                    asPhoto["kind"] = "photo";
                    var bytes = await BytesAsync(accountKey, new JsonObject { ["_acc"] = card.Str("_acc"), ["media"] = new JsonObject { ["m"] = asPhoto } }, ct).ConfigureAwait(false);
                    if (bytes.IsOk && m is JsonObject entry)
                    {
                        entry["uri"] = $"data:{bytes.Value.MediaType};base64,{Convert.ToBase64String(bytes.Value.Bytes)}";
                        _ = entry.Remove("blobId");
                    }
                    else
                    {
                        _ = media.Remove(k);
                    }
                }
            }

            output.Add(card);
        }

        return Result<IReadOnlyList<JsonNode>>.Ok(output);
    }

    /// <summary>The key of the card's first photo entry.</summary>
    private static string? PhotoKey(JsonNode? card) => ContactJson.Get(card, "media") is JsonObject m ? m.FirstOrDefault(kv => string.Equals(kv.Value.Str("kind"), "photo", StringComparison.Ordinal)).Key : null;

    private static string FreeKey(JsonNode card) => ContactJson.Get(card, "media") is JsonObject m ? ContactJson.FreeKey(m, "m") : "m0";

    private static bool MediaRefused(JmapError? error) => error is SetError e && (e.Properties?.Any(p => p.StartsWith("media", StringComparison.Ordinal)) == true || string.Equals(e.Type, "invalidPatch", StringComparison.Ordinal));
}
