// Mirrors: RFC 8621 section 4.9 (Email/parse: parsed by blob id, notParsable, notFound); Stalwart crates/jmap/src/email/parse.rs. Filled by
// the owner (unified, M2 plan 3.1): a blob is read as a plain message (headers, a blank line, a text body), enough for the reader's view of
// an attached message; parsed emails carry no id, threadId, mailboxIds or keywords (RFC 8621 §4.9).

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) EmailParse(JsonObject args)
    {
        var parsed = new JsonObject();
        var notParsable = new JsonArray();
        var notFound = new JsonArray();
        foreach (var id in (args["blobIds"] as JsonArray ?? []).Select(n => (string?)n).OfType<string>())
        {
            if (store.Blob(id) is not { } bytes)
            {
                notFound.Add((JsonNode)id);
                continue;
            }

            var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
            var split = text.IndexOf("\n\n", StringComparison.Ordinal);
            if (split < 0)
            {
                notParsable.Add((JsonNode)id);
                continue;
            }

            parsed[id] = Parsed(id, text[..split], text[(split + 2)..], bytes.Length);
        }

        return ("Email/parse", new JsonObject { ["accountId"] = options.AccountId, ["parsed"] = parsed, ["notParsable"] = notParsable, ["notFound"] = notFound });
    }

    private static JsonObject Parsed(string blobId, string head, string body, int size)
    {
        string? Header(string name) => head.Split('\n').FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim();
        static JsonArray Addresses(string? value) => value is null ? [] : new JsonArray(new JsonObject { ["name"] = null, ["email"] = value.Trim('<', '>', ' ') });
        var sent = DateTimeOffset.TryParse(Header("Date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : null;
        return new JsonObject
        {
            ["blobId"] = blobId,
            ["size"] = size,
            ["subject"] = Header("Subject") ?? "",
            ["from"] = Addresses(Header("From")),
            ["to"] = Addresses(Header("To")),
            ["sentAt"] = sent,
            ["receivedAt"] = sent,
            ["messageId"] = Header("Message-ID") is { } mid ? new JsonArray((JsonNode)mid.Trim('<', '>')) : null,
            ["preview"] = body.Length > 256 ? body[..256] : body,
            ["textBody"] = new JsonArray(new JsonObject { ["partId"] = "1", ["type"] = "text/plain", ["size"] = body.Length }),
            ["htmlBody"] = new JsonArray(new JsonObject { ["partId"] = "1", ["type"] = "text/plain", ["size"] = body.Length }),
            ["attachments"] = new JsonArray(),
            ["bodyValues"] = new JsonObject { ["1"] = new JsonObject { ["value"] = body, ["isEncodingProblem"] = false, ["isTruncated"] = false } },
            ["hasAttachment"] = false,
        };
    }
}
