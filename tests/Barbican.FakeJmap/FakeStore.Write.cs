// The write side of the fake store for Mailbox/set, Email/import and Email/copy: RFC 8621 §2.5 (parentId, name, sortOrder, destroy with
// mailboxHasChild / mailboxHasEmail / onDestroyRemoveEmails; role folders can't be renamed or destroyed), §4.7, §4.8.
// Stalwart crates/jmap/src/{mailbox/set,email/copy,email/import}.rs. Owner actions.

using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeStore
{
    /// <summary>Applies a Mailbox/set update; the SetError type when it is refused, else null.</summary>
    public string? UpdateMailbox(string id, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        lock (_lock)
        {
            var m = _mailboxes.Find(x => string.Equals((string?)x["id"], id, StringComparison.Ordinal));
            if (m is null)
            {
                return "notFound";
            }

            var hasRole = !string.IsNullOrEmpty((string?)m["role"]);
            foreach (var (key, value) in patch)
            {
                switch (key)
                {
                    case "name" when hasRole:
                        return "forbidden";
                    case "name":
                        m["name"] = value?.DeepClone();
                        break;
                    case "sortOrder":
                    case "isSubscribed":
                        m[key] = value?.DeepClone();
                        break;
                    case "parentId":
                        var parent = (string?)value;
                        if (parent is not null && (string.Equals(parent, id, StringComparison.Ordinal) || IsDescendant(parent, id)))
                        {
                            return "invalidProperties";
                        }

                        m["parentId"] = parent;
                        break;
                    default:
                        return "invalidProperties";
                }
            }

            Commit([new("Mailbox", id, "updated")]);
            return null;
        }
    }

    /// <summary>Destroys a folder; the SetError type when it is refused, else null.</summary>
    public string? DestroyMailbox(string id, bool removeEmails)
    {
        lock (_lock)
        {
            var m = _mailboxes.Find(x => string.Equals((string?)x["id"], id, StringComparison.Ordinal));
            if (m is null)
            {
                return "notFound";
            }

            if (!string.IsNullOrEmpty((string?)m["role"]))
            {
                return "forbidden";
            }

            if (_mailboxes.Exists(x => string.Equals((string?)x["parentId"], id, StringComparison.Ordinal)))
            {
                return "mailboxHasChild";
            }

            var inside = _emails.Values.Where(e => ((JsonObject)e["mailboxIds"]!).ContainsKey(id)).ToList();
            if (inside.Count > 0 && !removeEmails)
            {
                return "mailboxHasEmail";
            }

            _mailboxes.Remove(m);
            var changes = new List<PendingChange> { new("Mailbox", id, "destroyed") };
            foreach (var e in inside)
            {
                var boxes = (JsonObject)e["mailboxIds"]!;
                boxes.Remove(id);
                if (boxes.Count == 0)
                {
                    _emails.Remove((string)e["id"]!);
                    changes.Add(new("Email", (string)e["id"]!, "destroyed"));
                }
                else
                {
                    changes.Add(new("Email", (string)e["id"]!, "updated"));
                }
            }

            Commit(changes);
            return null;
        }
    }

    /// <summary>Whether a folder with this id exists.</summary>
    public bool HasMailbox(string id)
    {
        lock (_lock)
        {
            return _mailboxes.Exists(x => string.Equals((string?)x["id"], id, StringComparison.Ordinal));
        }
    }

    /// <summary>Email/copy inside the fake account: a new message with the same content in the given folders; null when the source is gone.</summary>
    public string? CopyEmail(string sourceId, IReadOnlyList<string> mailboxIds, IReadOnlyList<string> keywords)
    {
        lock (_lock)
        {
            if (!_emails.TryGetValue(sourceId, out var src))
            {
                return null;
            }

            var id = NewId("e");
            var copy = (JsonObject)src.DeepClone();
            copy["id"] = id;
            copy["mailboxIds"] = new JsonObject(mailboxIds.Select(m => KeyValuePair.Create(m, (JsonNode?)true)));
            copy["keywords"] = new JsonObject(keywords.Select(k => KeyValuePair.Create(k, (JsonNode?)true)));
            _emails[id] = copy;
            Commit(
            [
                new("Email", id, "created"),
                new("Thread", (string)copy["threadId"]!, "updated"),
                .. mailboxIds.Select(b => new PendingChange("Mailbox", b, "updated", CountProperties)),
            ]);
            return id;
        }
    }

    /// <summary>Email/import of an uploaded blob (the raw message as UTF-8 text) into the given folders; null when the blob is unknown.</summary>
    public string? ImportBlob(string blobId, IReadOnlyList<string> mailboxIds, IReadOnlyList<string> keywords)
    {
        var bytes = Blob(blobId);
        if (bytes is null || mailboxIds.Count == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var subject = text.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))?["Subject:".Length..].Trim() ?? "";
        var body = text.Contains("\r\n\r\n", StringComparison.Ordinal) ? text[(text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..] : text;
        var messageId = text.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith("Message-ID:", StringComparison.OrdinalIgnoreCase))?["Message-ID:".Length..].Trim().Trim('<', '>');
        var id = Deliver(new FakeMail { Subject = subject, Text = body, Keywords = keywords, MessageId = string.IsNullOrEmpty(messageId) ? null : messageId }, mailboxIds[0]);
        if (mailboxIds.Count > 1)
        {
            Patch(id, e =>
            {
                foreach (var m in mailboxIds.Skip(1))
                {
                    ((JsonObject)e["mailboxIds"]!)[m] = true;
                }

                return true;
            });
        }

        return id;
    }

    private bool IsDescendant(string candidate, string ancestor)
    {
        for (var cur = candidate; cur is not null;)
        {
            if (string.Equals(cur, ancestor, StringComparison.Ordinal))
            {
                return true;
            }

            cur = (string?)_mailboxes.Find(x => string.Equals((string?)x["id"], cur, StringComparison.Ordinal))?["parentId"];
        }

        return false;
    }
}
