// How the fake store shapes a message: RFC 8621 §4.1 (Email properties), §4.1.4 (EmailBodyPart), §4.1.3 (bodyValues), and the
// `header:Authentication-Results:all` form (§4.1.3 header parsed forms). Owner jmap.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeStore
{
    private JsonObject Build(string id, string threadId, string mailboxId, FakeMail mail)
    {
        var parts = Parts(mail, out var text, out var html, out var bodyValues);
        var size = Encoding.UTF8.GetByteCount(mail.Text) + Encoding.UTF8.GetByteCount(mail.Html ?? "") + mail.Attachments.Sum(a => a.Content.Length);
        var preview = mail.Text.Replace('\n', ' ');
        var messageId = id + "@example.org";
        var parentRef = mail.ReplyTo is { } r && _emails.TryGetValue(r, out var p) ? (string?)((JsonArray)p["messageId"]!)[0] : null;
        return new JsonObject
        {
            ["id"] = id,
            ["blobId"] = PutBlobLocked(Encoding.UTF8.GetBytes(mail.Text)),
            ["threadId"] = threadId,
            ["mailboxIds"] = new JsonObject { [mailboxId] = true },
            ["keywords"] = new JsonObject(mail.Keywords.Select(k => KeyValuePair.Create(k, (JsonNode?)true))),
            ["size"] = size,
            ["receivedAt"] = mail.ReceivedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["sentAt"] = mail.ReceivedAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["from"] = new JsonArray(Address(mail.FromName, mail.FromEmail)),
            ["to"] = new JsonArray(Address(null, mail.To)),
            ["cc"] = new JsonArray(),
            ["bcc"] = null,
            ["replyTo"] = null,
            ["sender"] = null,
            ["subject"] = mail.Subject,
            ["preview"] = preview.Length > 256 ? preview[..256] : preview,
            ["hasAttachment"] = mail.Attachments.Any(a => a.Cid is null),
            ["attachments"] = parts,
            ["textBody"] = text,
            ["htmlBody"] = html,
            ["bodyValues"] = bodyValues,
            ["messageId"] = new JsonArray(messageId),
            ["inReplyTo"] = parentRef is null ? null : new JsonArray(parentRef),
            ["references"] = parentRef is null ? null : new JsonArray(parentRef),
            ["headers"] = new JsonArray(new JsonObject { ["name"] = "Subject", ["value"] = " " + mail.Subject }),
            ["header:Authentication-Results:all"] = new JsonArray("mx.example.org; dkim=pass header.d=example.org; spf=pass; dmarc=pass"),
        };
    }

    private JsonArray Parts(FakeMail mail, out JsonArray text, out JsonArray html, out JsonObject bodyValues)
    {
        text = [Part("1", PutBlobLocked(Encoding.UTF8.GetBytes(mail.Text)), "text/plain", Encoding.UTF8.GetByteCount(mail.Text), null, null)];
        bodyValues = new JsonObject { ["1"] = new JsonObject { ["value"] = mail.Text, ["isEncodingProblem"] = false, ["isTruncated"] = false } };
        if (mail.Html is { } h)
        {
            html = [Part("2", PutBlobLocked(Encoding.UTF8.GetBytes(h)), "text/html", Encoding.UTF8.GetByteCount(h), null, null)];
            bodyValues["2"] = new JsonObject { ["value"] = h, ["isEncodingProblem"] = false, ["isTruncated"] = false };
        }
        else
        {
            html = [text[0]!.DeepClone()];
        }

        var attachments = new JsonArray();
        var n = 3;
        foreach (var a in mail.Attachments)
        {
            attachments.Add(Part((n++).ToString(CultureInfo.InvariantCulture), PutBlobLocked(a.Content), a.Type, a.Content.Length, a.Name, a.Cid));
        }

        return attachments;
    }

    private static JsonObject Part(string partId, string blobId, string type, long size, string? name, string? cid) => new()
    {
        ["partId"] = partId,
        ["blobId"] = blobId,
        ["size"] = size,
        ["name"] = name,
        ["type"] = type,
        ["charset"] = type.StartsWith("text/", StringComparison.Ordinal) ? "utf-8" : null,
        ["cid"] = cid,
        ["disposition"] = name is null ? null : cid is null ? "attachment" : "inline",
        ["language"] = null,
    };

    private static JsonObject Address(string? name, string email) => new() { ["name"] = name, ["email"] = email };

    private string PutBlobLocked(byte[] content)
    {
        var id = NewId("B");
        _blobs[id] = content;
        return id;
    }
}
