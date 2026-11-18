// Mirrors: Rust contacts_x.rs:565 shrink_photo, :540 save (the photo edit: upload a blob and set media with its blobId, the data: URI when the server refuses blobIds in media), :430 photo_bytes (data: URI here, blob from the server); Bulwark lib/contact-media.ts, lib/contact-photo-cache.ts.
// RFC 8620 6.1 (Blob/upload), Stalwart crates/jmap-proto/src/object/contact.rs (media). B-CON-014, B-CON-015, D-M5-18; G-012 (a link photo is never fetched, sizes are capped); J-036 (photo blobs). Owner con-data (D-M5-A08: a new interface so the frozen IContactWriter keeps its members).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Photos of contact cards: set, remove, read, and inline for export.</summary>
public interface IContactPhotos
{
    /// <summary>Shrinks the picture (<see cref="ContactPhoto.Shrink"/>), uploads it as a blob and sets it as the card's photo (the first photo entry is replaced, else a new one is added); a server that refuses a blob in media gets the picture as a data: URI instead.</summary>
    Task<Result<Unit>> SetAsync(string accountKey, string cardId, byte[] image, CancellationToken ct);

    /// <summary>Removes the card's photo.</summary>
    Task<Result<Unit>> RemoveAsync(string accountKey, string cardId, CancellationToken ct);

    /// <summary>The picture of a card's photo: from its data: URI, or downloaded from its blob (never more than <see cref="ContactPhoto.MaxInputBytes"/>); a photo that is a web link is not fetched (the address would tell its owner who looks).</summary>
    Task<Result<(string MediaType, byte[] Bytes)>> BytesAsync(string accountKey, JsonNode card, CancellationToken ct);

    /// <summary>Copies of the cards with every blob photo turned into a data: URI, for <see cref="VCardWriter.Generate"/> (a photo that cannot be downloaded is left out).</summary>
    Task<Result<IReadOnlyList<JsonNode>>> InlineForExportAsync(string accountKey, IReadOnlyList<JsonNode> cards, CancellationToken ct);
}
