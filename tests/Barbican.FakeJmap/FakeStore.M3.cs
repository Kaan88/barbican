// The M3 write side of the fake store: Email/set create of a composed message (RFC 8621 section 4.6: mailboxIds, keywords, addresses, bodyStructure with bodyValues and blob parts, header:Name:asText properties; blobNotFound for a part whose blob
// was never uploaded; Stalwart crates/jmap/src/email/set.rs) and Mailbox metadata patches (urn:ietf:params:jmap:metadata, Stalwart crates/jmap/src/mailbox/set.rs). Owner send-pipeline.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeStore
{
    /// <summary>Creates a message from an Email/set create object; the new id, or the SetError object.</summary>
    public (string? Id, JsonObject? Error) CreateEmail(JsonObject create, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(create);
        lock (_lock)
        {
            if (create["mailboxIds"] is not JsonObject boxes || boxes.Count == 0 || boxes.Any(b => FindMailboxOrNull(b.Key) is null))
            {
                return (null, new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("mailboxIds") });
            }

            var parts = new CreatedParts();
            if (create["bodyStructure"] is JsonObject structure && Walk(structure, create["bodyValues"] as JsonObject ?? [], parts) is { } missing)
            {
                return (null, new JsonObject { ["type"] = "blobNotFound", ["notFound"] = new JsonArray(missing) });
            }

            var id = NewId("e");
            var references = create["inReplyTo"] is JsonArray { Count: > 0 } irt ? (string?)irt[0] : null;
            var parent = references is null ? null : _emails.Values.FirstOrDefault(e => ((JsonArray)e["messageId"]!).Any(m => string.Equals((string?)m, references, StringComparison.Ordinal)));
            var threadId = parent is null ? NewId("t") : (string)parent["threadId"]!;
            var newThread = parent is null;
            var email = BuildCreated(id, threadId, create, parts, now);
            _emails[id] = email;
            Commit(
            [
                new("Email", id, "created"),
                new("Thread", threadId, newThread ? "created" : "updated"),
                .. Boxes(email).Select(b => new PendingChange("Mailbox", b, "updated", CountProperties)),
            ]);
            return (id, null);
        }
    }

    /// <summary>Applies <c>metadata/&lt;key&gt;</c> patches to a mailbox (a null value removes the entry); the SetError type when refused.</summary>
    public string? UpdateMailboxMetadata(string id, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        lock (_lock)
        {
            var m = FindMailboxOrNull(id);
            if (m is null)
            {
                return "notFound";
            }

            var meta = m["metadata"] as JsonObject ?? [];
            foreach (var (key, value) in patch)
            {
                if (!key.StartsWith("metadata/", StringComparison.Ordinal))
                {
                    return "invalidProperties";
                }

                var entry = key["metadata/".Length..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                if (value is null)
                {
                    meta.Remove(entry);
                }
                else
                {
                    meta[entry] = value.DeepClone();
                }
            }

            m["metadata"] = meta;
            Commit([new("Mailbox", id, "updated")]);
            return null;
        }
    }

    /// <summary>The metadata of a mailbox (a copy).</summary>
    public JsonObject MailboxMetadata(string id)
    {
        lock (_lock)
        {
            return FindMailboxOrNull(id)?["metadata"] is JsonObject m ? (JsonObject)m.DeepClone() : [];
        }
    }

    private JsonObject? FindMailboxOrNull(string id) => _mailboxes.Find(m => string.Equals((string?)m["id"], id, StringComparison.Ordinal));

    private sealed class CreatedParts
    {
        public JsonArray Text { get; } = [];

        public JsonArray Html { get; } = [];

        public JsonArray Attachments { get; } = [];

        public JsonObject Values { get; } = [];

        public int Size { get; set; }
    }

    // walks a bodyStructure; the id of the first blob that does not exist, else null
    private string? Walk(JsonObject part, JsonObject bodyValues, CreatedParts into)
    {
        if (part["subParts"] is JsonArray subs)
        {
            foreach (var sub in subs.OfType<JsonObject>())
            {
                if (Walk(sub, bodyValues, into) is { } missing)
                {
                    return missing;
                }
            }

            return null;
        }

        var type = (string?)part["type"] ?? "application/octet-stream";
        if ((string?)part["partId"] is { } partId && type.StartsWith("text/", StringComparison.Ordinal))
        {
            var value = (string?)bodyValues[partId]?["value"] ?? "";
            var bytes = Encoding.UTF8.GetBytes(value);
            var descriptor = Part(partId, PutBlobLocked(bytes), type, bytes.Length, null, null);
            (type.Equals("text/html", StringComparison.Ordinal) ? into.Html : into.Text).Add(descriptor);
            into.Values[partId] = new JsonObject { ["value"] = value, ["isEncodingProblem"] = false, ["isTruncated"] = false };
            into.Size += bytes.Length;
            return null;
        }

        if ((string?)part["blobId"] is { } blob)
        {
            if (!_blobs.TryGetValue(blob, out var content))
            {
                return blob;
            }

            var cid = (string?)part["cid"];
            var name = (string?)part["name"] ?? "attachment";
            var a = Part("a" + (into.Attachments.Count + 1).ToString(CultureInfo.InvariantCulture), blob, type, content.Length, name, cid);
            a["disposition"] = (string?)part["disposition"] ?? (cid is null ? "attachment" : "inline");
            into.Attachments.Add(a);
            into.Size += content.Length;
        }

        return null;
    }

    private JsonObject BuildCreated(string id, string threadId, JsonObject create, CreatedParts parts, DateTimeOffset now)
    {
        var text = parts.Text.Count > 0 ? parts.Text : parts.Html;
        var html = parts.Html.Count > 0 ? parts.Html : parts.Text;
        var subject = (string?)create["subject"] ?? "";
        var firstText = parts.Text.Count > 0 ? (string?)parts.Values[(string)parts.Text[0]!["partId"]!]?["value"] : null;
        var preview = (firstText ?? "").Replace('\n', ' ');
        var headers = new JsonArray(new JsonObject { ["name"] = "Subject", ["value"] = " " + subject });
        foreach (var (key, value) in create)
        {
            if (key.StartsWith("header:", StringComparison.Ordinal) && key.Split(':') is { Length: >= 2 } bits)
            {
                headers.Add((JsonNode)new JsonObject { ["name"] = bits[1], ["value"] = " " + (string?)value });
            }
        }

        var e = new JsonObject
        {
            ["id"] = id,
            ["blobId"] = PutBlobLocked(Encoding.UTF8.GetBytes(firstText ?? "")),
            ["threadId"] = threadId,
            ["mailboxIds"] = create["mailboxIds"]!.DeepClone(),
            ["keywords"] = create["keywords"]?.DeepClone() ?? new JsonObject(),
            ["size"] = parts.Size,
            ["receivedAt"] = now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["sentAt"] = now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["from"] = create["from"]?.DeepClone(),
            ["to"] = create["to"]?.DeepClone() ?? new JsonArray(),
            ["cc"] = create["cc"]?.DeepClone() ?? new JsonArray(),
            ["bcc"] = create["bcc"]?.DeepClone(),
            ["replyTo"] = create["replyTo"]?.DeepClone(),
            ["sender"] = null,
            ["subject"] = subject,
            ["preview"] = preview.Length > 256 ? preview[..256] : preview,
            ["hasAttachment"] = parts.Attachments.Any(a => a?["cid"] is null),
            ["attachments"] = parts.Attachments,
            ["textBody"] = text.DeepClone(),
            ["htmlBody"] = html.DeepClone(),
            ["bodyValues"] = parts.Values,
            ["messageId"] = new JsonArray(id + "@example.org"),
            ["inReplyTo"] = create["inReplyTo"]?.DeepClone(),
            ["references"] = create["references"]?.DeepClone(),
            ["headers"] = headers,
            ["header:Authentication-Results:all"] = new JsonArray(),
        };
        return e;
    }
}
