// Mirrors: RFC 8621 §4.9 (Email/parse: blobIds in, parsed emails by blob id out, notParsable and notFound lists; the same body properties
// and body values as Email/get), Stalwart crates/jmap/src/email/parse.rs; Rust reader.rs:728 is_message_attachment (message/rfc822 or a
// .eml name opens as a message). M2 plan 4.5 (IAttachedMessageOpener), spec J-058. Owner unified (the reader consumes it).
// A parsed message has no id of its own: it is given its blob id, which no Email/get or Email/set ever names, so nothing can act on it.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Parse;

/// <summary>Opens an attached message through Email/parse on the account that holds it.</summary>
/// <param name="clientOf">The client of an account key, or null when it is not signed in.</param>
public sealed class EmailParseOpener(Func<string, IJmapClient?> clientOf) : IAttachedMessageOpener
{
    /// <inheritdoc />
    /// <remarks>Email/parse is part of the mail capability (RFC 8621 §4.9): every mail account offers it.</remarks>
    public bool IsAvailable => true;

    /// <summary>True for an attachment that is a message (message/rfc822, or a .eml name).</summary>
    public static bool IsMessage(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return string.Equals(attachment.Type, "message/rfc822", StringComparison.OrdinalIgnoreCase)
            || (attachment.Name?.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <inheritdoc />
    public async Task<Result<EmailBody>> OpenAsync(MailTarget container, AttachmentRef attachment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(attachment);
        if (attachment.Attachment.BlobId is not { Length: > 0 } blobId)
        {
            return Result<EmailBody>.Fail(new ValidationError("This attachment cannot be opened."));
        }

        if (clientOf(container.Login) is not { } client)
        {
            return Result<EmailBody>.Fail(new ValidationError("That account is not signed in."));
        }

        var b = new JmapRequestBuilder();
        var parse = b.Add("Email/parse", new JsonObject
        {
            ["accountId"] = container.JmapAccountId(client.Session.MailAccountId),
            ["blobIds"] = new JsonArray(blobId),
            ["properties"] = JmapProperties.Json([.. JmapProperties.EmailFull.Where(p => p is not ("id" or "threadId" or "mailboxIds" or "keywords"))]),
            ["bodyProperties"] = JmapProperties.Json(JmapProperties.Body),
            ["fetchTextBodyValues"] = true,
            ["fetchHTMLBodyValues"] = true,
            ["maxBodyValueBytes"] = JmapProperties.MaxBodyValueBytes,
        });
        var sent = await client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        var args = sent.IsOk ? sent.Value!.Args(parse.CallId, parse.Method) : Result<JsonObject>.Fail(sent.Error!);
        if (!args.IsOk)
        {
            return Result<EmailBody>.Fail(args.Error!);
        }

        if (args.Value!["parsed"]?[blobId] is not JsonObject parsed)
        {
            return Result<EmailBody>.Fail(new ValidationError("The attached message could not be read."));
        }

        var email = (JsonObject)parsed.DeepClone();
        email["id"] ??= blobId;
        email["blobId"] ??= blobId;
        return ModelReader.Body(email) is { } body ? Result<EmailBody>.Ok(body) : Result<EmailBody>.Fail(new ValidationError("The attached message could not be read."));
    }
}
