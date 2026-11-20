// Mirrors: Rust jmap.rs:845 build_email (keywords, from, recipients, reply headers, multipart structure with the inline parts in multipart/related); Bulwark lib/jmap/client.ts createDraft/sendEmail :5560-5730
// (Disposition-Notification-To as text, lib/message-priority.ts messagePriorityHeaders, relatedBodyStructure #1451); RFC 8621 section 4.6 (Email/set create), section 4.1.4 (bodyStructure, bodyValues). Stalwart crates/jmap/src/email/set.rs.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>The Email/set create object of a composed message (pure).</summary>
internal static class DraftBuilder
{
    /// <summary>The create object for <paramref name="draft"/> in <paramref name="mailboxId"/>; <paramref name="asDraft"/> adds <c>$draft</c>.</summary>
    public static JsonObject Build(ComposeDraft draft, Identity identity, string username, string mailboxId, bool asDraft, string html, string text)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(identity);
        var from = SubmissionRules.HeaderFrom(Outgoing(draft), identity, username);
        var keywords = new JsonObject { ["$seen"] = true };
        foreach (var keyword in OutgoingKeywords.Clean(draft.Keywords)) keywords[keyword] = true;
        if (asDraft)
        {
            keywords["$draft"] = true;
        }

        var email = new JsonObject
        {
            ["mailboxIds"] = new JsonObject { [mailboxId] = true },
            ["keywords"] = keywords,
            ["from"] = new JsonArray(Address(from)),
            ["to"] = Addresses(draft.To),
            ["subject"] = draft.Subject,
        };
        AddOptionalFields(email, draft, identity, from);
        email["bodyValues"] = BodyValues(draft, html, text, out var structure);
        email["bodyStructure"] = structure;
        return email;
    }

    /// <summary>The address of each recipient in a field text, or none.</summary>
    public static IReadOnlyList<string> Recipients(ComposeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return [.. AddressParser.Parse(draft.To + "," + draft.Cc + "," + draft.Bcc).Select(a => a.Email)];
    }

    /// <summary>The pure sender view <see cref="SubmissionRules"/> works on.</summary>
    public static Outgoing Outgoing(ComposeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new Outgoing
        {
            From = draft.IdentityId,
            To = draft.To,
            Cc = draft.Cc,
            Bcc = draft.Bcc,
            Subject = draft.Subject,
            FromOverride = draft.FromOverride,
        };
    }

    private static void AddOptionalFields(JsonObject email, ComposeDraft draft, Identity identity, EmailAddress from)
    {
        if (draft.Cc.Trim().Length > 0)
        {
            email["cc"] = Addresses(draft.Cc);
        }

        if (draft.Bcc.Trim().Length > 0)
        {
            email["bcc"] = Addresses(draft.Bcc);
        }

        if (identity.ReplyTo is { Count: > 0 } replyTo)
        {
            email["replyTo"] = new JsonArray([.. replyTo.Select(Address)]);
        }

        if (draft.InReplyTo is { Length: > 0 } irt)
        {
            email["inReplyTo"] = new JsonArray(irt.Trim('<', '>'));
        }

        if (draft.References.Count > 0)
        {
            email["references"] = new JsonArray([.. draft.References.Select(r => (JsonNode?)JsonValue.Create(r.Trim('<', '>')))]);
        }

        if (draft.ReadReceipt)
        {
            email["header:Disposition-Notification-To:asText"] = from.Email;
        }

        switch (draft.Priority)
        {
            case MessagePriority.High:
                email["header:X-Priority:asText"] = "1 (Highest)";
                email["header:Importance:asText"] = "high";
                break;
            case MessagePriority.Low:
                email["header:X-Priority:asText"] = "5 (Lowest)";
                email["header:Importance:asText"] = "low";
                break;
            default:
                break;
        }
    }

    private static JsonObject BodyValues(ComposeDraft draft, string html, string text, out JsonObject structure)
    {
        var inline = draft.PlainText ? [] : draft.Attachments.Where(a => a.Cid is not null && a.BlobId is not null && html.Contains("cid:" + a.Cid, StringComparison.Ordinal)).ToList();
        var files = draft.Attachments.Where(a => a.Cid is null && a.BlobId is not null).ToList();
        var values = new JsonObject { ["t"] = new JsonObject { ["value"] = text } };
        var textPart = new JsonObject { ["partId"] = "t", ["type"] = "text/plain" };
        JsonObject body;
        if (draft.PlainText)
        {
            body = textPart;
        }
        else
        {
            values["h"] = new JsonObject { ["value"] = html };
            var htmlPart = new JsonObject { ["partId"] = "h", ["type"] = "text/html" };
            if (inline.Count > 0)
            {
                var related = new JsonArray(htmlPart);
                foreach (var a in inline)
                {
                    related.Add((JsonNode)new JsonObject { ["blobId"] = a.BlobId, ["type"] = a.Type, ["name"] = a.Name, ["disposition"] = "inline", ["cid"] = a.Cid });
                }

                htmlPart = new JsonObject { ["type"] = "multipart/related", ["subParts"] = related };
            }

            body = new JsonObject { ["type"] = "multipart/alternative", ["subParts"] = new JsonArray(textPart, htmlPart) };
        }

        if (files.Count == 0)
        {
            structure = body;
            return values;
        }

        var parts = new JsonArray(body);
        foreach (var a in files)
        {
            parts.Add((JsonNode)new JsonObject { ["blobId"] = a.BlobId, ["type"] = a.Type, ["name"] = a.Name, ["disposition"] = "attachment" });
        }

        structure = new JsonObject { ["type"] = "multipart/mixed", ["subParts"] = parts };
        return values;
    }

    private static JsonArray Addresses(string field) => new([.. AddressParser.Parse(field).Select(Address)]);

    private static JsonObject Address(EmailAddress a)
    {
        var o = new JsonObject { ["email"] = a.Email };
        if (!string.IsNullOrWhiteSpace(a.Name))
        {
            o["name"] = a.Name;
        }

        return o;
    }
}
