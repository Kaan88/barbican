// Mirrors: Rust main.rs:617 inline_cid_images (tag rust-final): cid: references in the HTML parts become data: URIs of the inline parts,
// the original HTML is kept (a reply or forward quotes it and sends the parts inline again), parts over 5 MB stay cid:. RFC 2392 (cid: URLs),
// RFC 2397 (data: URLs), RFC 8621 §4.1.4 (attachments with cid). Bulwark components/email/email-viewer.tsx resolves cid: to blob: URLs
// instead; the frame CSP here allows no blob: (MailCsp), so data: it is. Owner body.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Body;

/// <summary>Puts the inline images of a message into its HTML as <c>data:</c> URIs.</summary>
public static class CidImages
{
    /// <summary>An inline image larger than this stays a <c>cid:</c> reference.</summary>
    public const long MaxBytes = 5_000_000;

    private static readonly System.Buffers.SearchValues<char> TypeChars = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789.+-");

    /// <summary>
    /// The message with every resolvable <c>cid:</c> reference of its HTML parts replaced; <see cref="EmailBody.CidBodyValues"/> keeps the
    /// original HTML per part id. A part whose download fails (null) keeps its reference.
    /// </summary>
    public static async Task<EmailBody> InlineAsync(EmailBody message, Func<Attachment, CancellationToken, Task<byte[]?>> download, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(download);
        // longest content id first: "cid:logo" is a prefix of "cid:logo2", and replacing it first would break the longer reference (Rust
        // replaced in part order and had this bug); a data: URI holds no "cid:", so a replaced reference is never matched again
        var parts = message.Attachments.Where(a => a.Cid is not null && SafeImageType(a.Type)).OrderByDescending(a => a.Cid!.Trim('<', '>').Length).ToList();
        if (parts.Count == 0)
        {
            return message;
        }

        var values = new Dictionary<string, BodyValue>(message.BodyValues, StringComparer.Ordinal);
        var originals = new Dictionary<string, string>(message.CidBodyValues, StringComparer.Ordinal);
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var changed = false;
        foreach (var part in message.HtmlBody)
        {
            if (part.PartId is not { } pid || !values.TryGetValue(pid, out var value) || !value.Value.Contains("cid:", StringComparison.Ordinal))
            {
                continue;
            }

            originals.TryAdd(pid, value.Value);
            var html = value.Value;
            foreach (var p in parts)
            {
                var cid = p.Cid!.Trim('<', '>');
                var reference = "cid:" + cid;
                if (!html.Contains(reference, StringComparison.Ordinal) || p.Size > MaxBytes)
                {
                    continue;
                }

                if (!cache.TryGetValue(cid, out var uri))
                {
                    var bytes = await download(p, ct).ConfigureAwait(false);
                    uri = bytes is null ? null : $"data:{p.Type};base64,{Convert.ToBase64String(bytes)}";
                    cache[cid] = uri;
                }

                if (uri is not null)
                {
                    html = html.Replace(reference, uri, StringComparison.Ordinal);
                }
            }

            values[pid] = value with { Value = html };
            changed = true;
        }

        return changed ? message with { BodyValues = values, CidBodyValues = originals } : message;
    }

    // the type goes into the HTML as it is: only image/<token> may (a quote in it would end the attribute)
    private static bool SafeImageType(string? type) =>
        type is { Length: > 6 and <= 100 } t && t.StartsWith("image/", StringComparison.Ordinal)
        && t.AsSpan(6).IndexOfAnyExcept(TypeChars) < 0;
}
