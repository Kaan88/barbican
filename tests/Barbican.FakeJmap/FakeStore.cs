// The fake server's data: mailboxes, messages, blobs and the change log, with Stalwart state strings (an account-wide change id,
// Stalwart crates/jmap-proto/src/types/state.rs; RFC 8620 §5.2) and a snapshot of the message index at each Email state so
// /queryChanges can be calculated (RFC 8620 §5.6). Owner jmap.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.State;

namespace Barbican.FakeJmap;

/// <summary>An in-memory JMAP account.</summary>
public sealed partial class FakeStore
{
    /// <summary>The properties a counts-only Mailbox update names (Stalwart updatedProperties).</summary>
    public static readonly IReadOnlyList<string> CountProperties = ["totalEmails", "unreadEmails", "totalThreads", "unreadThreads"];

    private readonly Lock _lock = new();
    private readonly List<JsonObject> _mailboxes = [];
    private readonly Dictionary<string, JsonObject> _emails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
    private readonly List<FakeChange> _log = [];
    private readonly Dictionary<string, ulong> _typeState = new(StringComparer.Ordinal) { ["Email"] = 1, ["Mailbox"] = 1, ["Thread"] = 1, ["EmailDelivery"] = 1 };
    private readonly Dictionary<ulong, IReadOnlyList<JsonObject>> _snapshots = [];
    private ulong _change = 1;
    private int _nextId;

    public FakeStore()
    {
        _snapshots[1] = [];
    }

    /// <summary>Raised after each change with the changed types and their new states (the push hub listens).</summary>
    public event EventHandler<FakeStateChangedEventArgs>? Changed;

    /// <summary>The current state of a data type.</summary>
    public string State(string type)
    {
        lock (_lock)
        {
            return StalwartState.Encode(_typeState.TryGetValue(type, out var s) ? s : 1);
        }
    }

    /// <summary>A folder by role.</summary>
    public string? MailboxId(string role)
    {
        lock (_lock)
        {
            return _mailboxes.Find(m => string.Equals((string?)m["role"], role, StringComparison.Ordinal))?["id"]?.GetValue<string>();
        }
    }

    /// <summary>Every message id, newest first.</summary>
    public IReadOnlyList<string> EmailIds()
    {
        lock (_lock)
        {
            return [.. _emails.Values.OrderByDescending(e => (string?)e["receivedAt"], StringComparer.Ordinal).Select(e => (string)e["id"]!)];
        }
    }

    /// <summary>A copy of a message's full object.</summary>
    public JsonObject? Email(string id)
    {
        lock (_lock)
        {
            return _emails.TryGetValue(id, out var e) ? (JsonObject)e.DeepClone() : null;
        }
    }

    /// <summary>A blob's bytes.</summary>
    public byte[]? Blob(string id)
    {
        lock (_lock)
        {
            return _blobs.TryGetValue(id, out var b) ? b : null;
        }
    }

    /// <summary>Stores an uploaded blob.</summary>
    public string PutBlob(byte[] content)
    {
        lock (_lock)
        {
            var id = NewId("B");
            _blobs[id] = content;
            return id;
        }
    }

    /// <summary>Adds a folder.</summary>
    public string AddMailbox(string name, string? role, string? parentId = null)
    {
        lock (_lock)
        {
            var id = NewId("m");
            _mailboxes.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["parentId"] = parentId,
                ["role"] = role,
                ["sortOrder"] = _mailboxes.Count,
                ["isSubscribed"] = true,
                ["myRights"] = Rights(),
            });
            Commit([new("Mailbox", id, "created")]);
            return id;
        }
    }

    /// <summary>Renames a folder (a full Mailbox update: updatedProperties null).</summary>
    public void RenameMailbox(string id, string name)
    {
        lock (_lock)
        {
            FindMailbox(id)["name"] = name;
            Commit([new("Mailbox", id, "updated")]);
        }
    }

    /// <summary>Delivers a message into <paramref name="mailboxId"/> (default the inbox).</summary>
    public string Deliver(FakeMail mail, string? mailboxId = null)
    {
        ArgumentNullException.ThrowIfNull(mail);
        lock (_lock)
        {
            var box = mailboxId ?? _mailboxes.Find(m => string.Equals((string?)m["role"], "inbox", StringComparison.Ordinal))?["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException("no inbox");
            var id = NewId("e");
            var threadId = mail.ReplyTo is { } r && _emails.TryGetValue(r, out var parent) ? (string)parent["threadId"]! : NewId("t");
            var newThread = !_emails.Values.Any(e => string.Equals((string?)e["threadId"], threadId, StringComparison.Ordinal));
            _emails[id] = Build(id, threadId, box, mail);
            Commit(
            [
                new("Email", id, "created"),
                new("Thread", threadId, newThread ? "created" : "updated"),
                new("Mailbox", box, "updated", CountProperties),
                new("EmailDelivery", id, "created"),
            ]);
            return id;
        }
    }

    /// <summary>Sets or clears a keyword.</summary>
    public void SetKeyword(string id, string keyword, bool on)
    {
        lock (_lock)
        {
            var e = _emails[id];
            var k = (JsonObject)e["keywords"]!;
            if (on)
            {
                k[keyword] = true;
            }
            else
            {
                k.Remove(keyword);
            }

            Commit([new("Email", id, "updated"), .. Boxes(e).Select(b => new PendingChange("Mailbox", b, "updated", CountProperties))]);
        }
    }

    /// <summary>Moves a message to one folder.</summary>
    public void Move(string id, string mailboxId)
    {
        lock (_lock)
        {
            var e = _emails[id];
            var before = Boxes(e);
            e["mailboxIds"] = new JsonObject { [mailboxId] = true };
            Commit([new("Email", id, "updated"), .. before.Append(mailboxId).Distinct(StringComparer.Ordinal).Select(b => new PendingChange("Mailbox", b, "updated", CountProperties))]);
        }
    }

    /// <summary>Destroys a message.</summary>
    public void Destroy(string id)
    {
        lock (_lock)
        {
            var e = _emails[id];
            _emails.Remove(id);
            var threadId = (string)e["threadId"]!;
            var threadGone = !_emails.Values.Any(x => string.Equals((string?)x["threadId"], threadId, StringComparison.Ordinal));
            Commit(
            [
                new("Email", id, "destroyed"),
                new("Thread", threadId, threadGone ? "destroyed" : "updated"),
                .. Boxes(e).Select(b => new PendingChange("Mailbox", b, "updated", CountProperties)),
            ]);
        }
    }

    /// <summary>Applies an Email/set patch to a message; false (nothing changed) when the message is gone or the patch is refused.</summary>
    public bool Patch(string id, Func<JsonObject, bool> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        lock (_lock)
        {
            if (!_emails.TryGetValue(id, out var e))
            {
                return false;
            }

            var before = Boxes(e);
            var copy = (JsonObject)e.DeepClone();
            if (!apply(copy))
            {
                return false;
            }

            _emails[id] = copy;
            Commit([new("Email", id, "updated"), .. before.Union(Boxes(copy), StringComparer.Ordinal).Select(b => new PendingChange("Mailbox", b, "updated", CountProperties))]);
            return true;
        }
    }

    private static JsonObject Rights() => new()
    {
        ["mayReadItems"] = true, ["mayAddItems"] = true, ["mayRemoveItems"] = true, ["maySetSeen"] = true, ["maySetKeywords"] = true,
        ["mayCreateChild"] = true, ["mayRename"] = true, ["mayDelete"] = true, ["maySubmit"] = true,
    };

    private static List<string> Boxes(JsonObject e) => [.. ((JsonObject)e["mailboxIds"]!).Select(kv => kv.Key)];

    private JsonObject FindMailbox(string id) =>
        _mailboxes.Find(m => string.Equals((string?)m["id"], id, StringComparison.Ordinal)) ?? throw new KeyNotFoundException(id);

    private string NewId(string prefix) => prefix + (++_nextId).ToString(CultureInfo.InvariantCulture);

    private void Commit(IReadOnlyList<PendingChange> changes)
    {
        var id = ++_change;
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in changes)
        {
            if (!string.Equals(c.Type, "EmailDelivery", StringComparison.Ordinal))
            {
                _log.Add(new FakeChange(id, c.Type, c.Id, c.Kind, c.Properties));
            }

            _typeState[c.Type] = id;
            states[c.Type] = StalwartState.Encode(id);
        }

        if (states.ContainsKey("Email"))
        {
            _snapshots[id] = [.. _emails.Values.Select(Index)];
        }

        Changed?.Invoke(this, new FakeStateChangedEventArgs(states));
    }

    private static JsonObject Index(JsonObject e)
    {
        var o = new JsonObject();
        foreach (var key in new[] { "id", "threadId", "mailboxIds", "keywords", "receivedAt", "subject", "preview", "from", "to" })
        {
            o[key] = e[key]?.DeepClone();
        }

        return o;
    }

    private sealed record PendingChange(string Type, string Id, string Kind, IReadOnlyList<string>? Properties = null);
}
