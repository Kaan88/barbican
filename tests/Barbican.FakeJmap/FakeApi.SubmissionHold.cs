// The fake server's M3 send arms: Email/set create (RFC 8621 4.6, FakeStore.CreateEmail) with update and destroy in the same call; Identity/get with signatures (section 6); EmailSubmission/set, get and query (section 7: creation ids such as "#d" for
// emailId, an explicit envelope, HOLDFOR in the mailFrom parameters when the capability lists FUTURERELEASE (RFC 4865) and the hold is within maxDelayedSend and Stalwart's 7 days, undoStatus pending -> final when the held time has come on the
// server's clock, pending -> canceled by an update, cannotUnsend for a final one, onSuccessUpdateEmail applied when the submission is created, forbiddenFrom for a MAIL FROM the identity does not own when StrictMailFrom is on).
// Stalwart crates/jmap/src/submission/{set,get,query}.rs and crates/jmap/src/email/set.rs. The M2 rules stay: a recipient at refused.example is refused with invalidRecipients (the MdnSender path). Owner send-pipeline.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private const long StalwartFutureReleaseLimit = 7L * 86400;

    private readonly List<HeldSubmission> _held = [];
    private readonly List<string> _released = [];
    private int _submissionState;

    [ThreadStatic]
    private static Dictionary<string, string>? _creations;

    /// <summary>The ids of the messages whose held submissions have been released (sent), in order.</summary>
    public IReadOnlyList<string> Released
    {
        get
        {
            lock (_lock)
            {
                ReleaseDue();
                return [.. _released];
            }
        }
    }

    /// <summary>Every submission the server holds or has held: id, email id, undoStatus, sendAt (null for a send at once).</summary>
    public IReadOnlyList<(string Id, string EmailId, string UndoStatus, DateTimeOffset? SendAt)> SubmissionStates
    {
        get
        {
            lock (_lock)
            {
                ReleaseDue();
                return [.. _held.Select(h => (h.Id, h.EmailId, h.UndoStatus, h.SendAt))];
            }
        }
    }

    /// <summary>Test hook: MAIL FROM addresses (case-insensitive) the server refuses with forbiddenFrom, whatever the identity.</summary>
    public ISet<string> ForbiddenMailFrom { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Test hook: called at the start of every Mailbox/set (a second device changing the Inbox metadata between a read and a write).</summary>
    public Action<JsonObject>? BeforeMailboxSet { get; set; }

    private DateTimeOffset Now => (options.Clock ?? TimeProvider.System).GetUtcNow();

    private static void ForgetCreations() => _creations = [];

    private static void RememberCreations(JsonObject response)
    {
        if (response["created"] is not JsonObject created)
        {
            return;
        }

        foreach (var (key, value) in created)
        {
            if ((string?)value?["id"] is { } id)
            {
                (_creations ??= [])["#" + key] = id;
            }
        }
    }

    private static string? ResolveCreation(string? reference) =>
        reference is not null && reference.StartsWith('#') && _creations is { } map && map.TryGetValue(reference, out var id) ? id : reference;

    private (string? Name, JsonObject Args) EmailSetWithCreate(JsonObject args)
    {
        var creates = args["create"] as JsonObject ?? [];
        var rest = new JsonObject();
        foreach (var (key, value) in args)
        {
            if (!string.Equals(key, "create", StringComparison.Ordinal))
            {
                rest[key] = value?.DeepClone();
            }
        }

        var created = new JsonObject();
        var notCreated = new JsonObject();
        foreach (var (key, value) in creates)
        {
            var (id, error) = store.CreateEmail(value as JsonObject ?? [], Now);
            if (id is not null)
            {
                created[key] = new JsonObject { ["id"] = id, ["blobId"] = (string?)store.Email(id)?["blobId"], ["threadId"] = (string?)store.Email(id)?["threadId"], ["size"] = (int?)store.Email(id)?["size"] };
            }
            else
            {
                notCreated[key] = error;
            }
        }

        var (name, response) = EmailSet(rest);
        if (name is null && creates.Count > 0)
        {
            response["created"] = created;
            response["notCreated"] = notCreated;
        }

        return (name, response);
    }

    private (string? Name, JsonObject Args) IdentityGetM3() => (null, new JsonObject
    {
        ["accountId"] = options.AccountId,
        ["state"] = "i1",
        ["list"] = new JsonArray([.. (options.Identities ?? []).Select(i => (JsonNode?)new JsonObject
        {
            ["id"] = i.Id,
            ["name"] = i.Name,
            ["email"] = i.Email,
            ["replyTo"] = i.ReplyTo is null ? null : new JsonArray(new JsonObject { ["email"] = i.ReplyTo }),
            ["bcc"] = null,
            ["textSignature"] = i.TextSignature,
            ["htmlSignature"] = i.HtmlSignature,
            ["mayDelete"] = false,
        })]),
        ["notFound"] = new JsonArray(),
    });

    private (string? Name, JsonObject Args) EmailSubmissionSetHeld(JsonObject args)
    {
        lock (_lock)
        {
            ReleaseDue();
            var oldState = SubmissionState();
            var created = new JsonObject();
            var notCreated = new JsonObject();
            foreach (var (key, value) in args["create"] as JsonObject ?? [])
            {
                var create = value as JsonObject ?? [];
                create["emailId"] = ResolveCreation((string?)create["emailId"]);
                var (record, error) = CreateSubmission(create);
                if (record is null)
                {
                    notCreated[key] = error;
                    continue;
                }

                created[key] = new JsonObject { ["id"] = record.Id, ["undoStatus"] = record.UndoStatus, ["sendAt"] = record.SendAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
                ApplyOnSuccess(args["onSuccessUpdateEmail"] as JsonObject, key, record.EmailId);
                // A submission creation reference names the associated Email, not the submission id.
                // Refused creations never reach this branch, so their imported message is retained.
                if (args["onSuccessDestroyEmail"] is JsonArray destroyEmails &&
                    destroyEmails.Any(item => string.Equals((string?)item, "#" + key, StringComparison.Ordinal)))
                {
                    store.Destroy(record.EmailId);
                }
            }

            var (updated, notUpdated) = UpdateSubmissions(args["update"] as JsonObject ?? []);
            var destroyed = new JsonArray();
            var notDestroyed = new JsonObject();
            foreach (var id in (args["destroy"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
            {
                if (_held.RemoveAll(h => string.Equals(h.Id, id, StringComparison.Ordinal)) > 0)
                {
                    destroyed.Add(id);
                    _submissionState++;
                }
                else
                {
                    notDestroyed[id] = new JsonObject { ["type"] = "notFound" };
                }
            }

            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["oldState"] = oldState,
                ["newState"] = SubmissionState(),
                ["created"] = created,
                ["notCreated"] = notCreated,
                ["updated"] = updated,
                ["notUpdated"] = notUpdated,
                ["destroyed"] = destroyed,
                ["notDestroyed"] = notDestroyed,
            });
        }
    }

    private (HeldSubmission? Record, JsonObject? Error) CreateSubmission(JsonObject create)
    {
        var emailId = (string?)create["emailId"];
        var identityId = (string?)create["identityId"];
        var identities = options.Identities ?? [new FakeIdentity("i1", "Test User", options.Username)];
        var identity = identities.FirstOrDefault(i => string.Equals(i.Id, identityId, StringComparison.Ordinal));
        if (emailId is null || store.Email(emailId) is null)
        {
            return (null, new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("emailId") });
        }

        if (identity is null)
        {
            return (null, new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("identityId") });
        }

        var envelope = create["envelope"] as JsonObject;
        var rcpts = (envelope?["rcptTo"] as JsonArray ?? RecipientsOf(store.Email(emailId)!)).Select(r => (string?)r?["email"]).ToList();
        if (rcpts.Count == 0 || rcpts.Any(r => r is null || r.EndsWith("@refused.example", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, new JsonObject { ["type"] = "invalidRecipients", ["invalidRecipients"] = new JsonArray([.. rcpts.Select(r => (JsonNode?)r)]) });
        }

        var mailFrom = (string?)envelope?["mailFrom"]?["email"] ?? identity.Email;
        if (ForbiddenMailFrom.Contains(mailFrom) || (options.StrictMailFrom && !string.Equals(mailFrom, identity.Email, StringComparison.OrdinalIgnoreCase) && !Covers(identity.Email, mailFrom)))
        {
            return (null, new JsonObject { ["type"] = "forbiddenFrom", ["description"] = "The sender address is not one of the identity's." });
        }

        var hold = Hold(envelope, out var refusal);
        if (refusal is not null)
        {
            return (null, refusal);
        }

        _submissions.Add((JsonObject)create.DeepClone());
        _submissionState++;
        var record = new HeldSubmission("S" + (_held.Count + 1 + _released.Count + _submissionState).ToString(CultureInfo.InvariantCulture), emailId, identityId!, (JsonObject?)envelope?.DeepClone(), hold is { } h ? Now.AddSeconds(h) : null)
        {
            UndoStatus = hold is > 0 ? "pending" : "final",
        };
        _held.Add(record);
        return (record, null);
    }

    private static JsonArray RecipientsOf(JsonObject email)
    {
        var all = new JsonArray();
        foreach (var key in new[] { "to", "cc", "bcc" })
        {
            foreach (var a in email[key] as JsonArray ?? [])
            {
                all.Add((JsonNode?)a?.DeepClone());
            }
        }

        return all;
    }

    private static bool Covers(string pattern, string address)
    {
        var p = pattern.Split('@');
        var a = address.Split('@');
        return p.Length == 2 && a.Length == 2 && string.Equals(p[0], "*", StringComparison.Ordinal) && string.Equals(p[1], a[1], StringComparison.OrdinalIgnoreCase);
    }

    // HOLDFOR seconds from the envelope's mailFrom parameters; refused when the server cannot hold or the hold is past its limits
    private long? Hold(JsonObject? envelope, out JsonObject? refusal)
    {
        refusal = null;
        var parameters = envelope?["mailFrom"]?["parameters"] as JsonObject;
        if (parameters is null || (string?)parameters["HOLDFOR"] is not { } raw)
        {
            return null;
        }

        var advertised = options.SubmissionExtensions.Any(e => string.Equals(e, "FUTURERELEASE", StringComparison.OrdinalIgnoreCase));
        if (!advertised || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds > options.MaxDelayedSend || seconds > StalwartFutureReleaseLimit)
        {
            refusal = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("envelope"), ["description"] = "HOLDFOR is not allowed." };
            return null;
        }

        return seconds;
    }

    // onSuccessUpdateEmail: {"#<creation id>": patch}, applied to the message of the submission just created
    private void ApplyOnSuccess(JsonObject? onSuccess, string creationKey, string emailId)
    {
        if (onSuccess?["#" + creationKey] is JsonObject patch)
        {
            _ = store.Patch(emailId, e => ApplyPatch(e, patch));
        }
    }

    private (JsonObject Updated, JsonObject NotUpdated) UpdateSubmissions(JsonObject update)
    {
        var updated = new JsonObject();
        var notUpdated = new JsonObject();
        foreach (var (id, patch) in update)
        {
            var record = _held.Find(h => string.Equals(h.Id, id, StringComparison.Ordinal));
            if (record is null)
            {
                notUpdated[id] = new JsonObject { ["type"] = "notFound" };
            }
            else if (!string.Equals((string?)patch?["undoStatus"], "canceled", StringComparison.Ordinal))
            {
                notUpdated[id] = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("undoStatus") };
            }
            else if (string.Equals(record.UndoStatus, "final", StringComparison.Ordinal))
            {
                notUpdated[id] = new JsonObject { ["type"] = "cannotUnsend" };
            }
            else
            {
                record.UndoStatus = "canceled";
                _submissionState++;
                updated[id] = null;
            }
        }

        return (updated, notUpdated);
    }

    private (string? Name, JsonObject Args) EmailSubmissionGet(JsonObject args)
    {
        lock (_lock)
        {
            ReleaseDue();
            var ids = (args["ids"] as JsonArray)?.Select(x => (string?)x).OfType<string>().ToList();
            var chosen = ids is null ? _held : [.. _held.Where(h => ids.Contains(h.Id, StringComparer.Ordinal))];
            var notFound = ids is null ? [] : ids.Where(i => !_held.Any(h => string.Equals(h.Id, i, StringComparison.Ordinal))).ToList();
            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["state"] = SubmissionState(),
                ["list"] = new JsonArray([.. chosen.Select(h => (JsonNode?)Project(h.ToJson(), args["properties"] as JsonArray))]),
                ["notFound"] = new JsonArray([.. notFound.Select(i => (JsonNode?)i)]),
            });
        }
    }

    private (string? Name, JsonObject Args) EmailSubmissionQuery(JsonObject args)
    {
        lock (_lock)
        {
            ReleaseDue();
            var wanted = (string?)args["filter"]?["undoStatus"];
            var ids = _held.Where(h => wanted is null || string.Equals(h.UndoStatus, wanted, StringComparison.Ordinal))
                .OrderBy(h => h.SendAt ?? DateTimeOffset.MinValue).Select(h => (JsonNode?)h.Id).ToList();
            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["queryState"] = SubmissionState(),
                ["canCalculateChanges"] = false,
                ["position"] = 0,
                ["ids"] = new JsonArray([.. ids]),
                ["total"] = ids.Count,
            });
        }
    }

    // the release job: a pending submission whose held time has come is final (sent)
    private void ReleaseDue()
    {
        foreach (var h in _held.Where(h => string.Equals(h.UndoStatus, "pending", StringComparison.Ordinal) && h.SendAt is { } at && at <= Now))
        {
            h.UndoStatus = "final";
            _released.Add(h.EmailId);
            _submissionState++;
        }
    }

    private string SubmissionState() => "es" + _submissionState.ToString(CultureInfo.InvariantCulture);

    private sealed class HeldSubmission(string id, string emailId, string identityId, JsonObject? envelope, DateTimeOffset? sendAt)
    {
        public string Id { get; } = id;

        public string EmailId { get; } = emailId;

        public DateTimeOffset? SendAt { get; } = sendAt;

        public string UndoStatus { get; set; } = "final";

        public JsonObject ToJson() => new()
        {
            ["id"] = Id,
            ["emailId"] = EmailId,
            ["identityId"] = identityId,
            ["envelope"] = envelope?.DeepClone(),
            ["sendAt"] = SendAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["undoStatus"] = UndoStatus,
        };
    }
}
