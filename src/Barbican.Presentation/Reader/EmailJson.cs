// The fields of a fetched message that the Rust-shaped helpers (mdn.rs, reader.rs ports in Core) read, as JMAP Email JSON (RFC 8621 §4.1). Owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Reader;

/// <summary>An <see cref="EmailBody"/> as JMAP Email JSON (id, subject, headers, messageId, from, to, cc, keywords, mailboxIds).</summary>
public static class EmailJson
{
    public static JsonObject Of(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        static JsonArray Addresses(IEnumerable<EmailAddress> list) => new([.. list.Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["email"] = a.Email })]);
        return new JsonObject
        {
            ["id"] = body.Id,
            ["subject"] = body.Subject,
            ["headers"] = new JsonArray([.. body.Headers.Select(h => (JsonNode)new JsonObject { ["name"] = h.Name, ["value"] = h.Value })]),
            ["messageId"] = new JsonArray([.. body.MessageId.Select(m => (JsonNode)m)]),
            ["from"] = Addresses(body.From),
            ["to"] = Addresses(body.To),
            ["cc"] = Addresses(body.Cc),
            ["keywords"] = new JsonObject([.. body.Keywords.Map.Where(k => k.Value).Select(k => new KeyValuePair<string, JsonNode?>(k.Key, true))]),
            ["mailboxIds"] = new JsonObject([.. body.MailboxIds.Where(m => m.Value).Select(m => new KeyValuePair<string, JsonNode?>(m.Key, true))]),
        };
    }
}
