// The fake server's Identity/set (RFC 8621 section 6.3: create with name, email, replyTo, bcc, textSignature, htmlSignature; update; destroy; Stalwart crates/jmap/src/identity/set.rs: an email that is not one of the account's addresses is invalidProperties, a default identity has mayDelete false and a destroy of it is forbidden; Bulwark lib/jmap/client.ts:5213-5287).
// It works on FakeJmapOptions.Identities (or the one M1 identity), bumps the Identity state, honours ifInState, and fails the next write with FailNextIdentitySet. Until the first write Identity/get answers exactly as the M3 and M1 arms did; after it the list is live. M6 plan section 4.12. Owner set-data.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private List<JsonObject>? _identityList;
    private int _identityStateNumber = 1;
    private int _identityCreated;
    private string? _identityFailure;

    /// <summary>Every Identity/set that arrived, with its arguments (a copy).</summary>
    public IReadOnlyList<JsonObject> IdentitySetRequests
    {
        get
        {
            lock (_lock)
            {
                return [.. _identitySetRequests.Select(r => (JsonObject)r.DeepClone())];
            }
        }
    }

    private readonly List<JsonObject> _identitySetRequests = [];

    /// <summary>The next Identity/set is refused object by object with this SetError type (for instance "forbidden" or "serverFail"); nothing changes.</summary>
    public void FailNextIdentitySet(string errorType)
    {
        lock (_lock)
        {
            _identityFailure = errorType;
        }
    }

    private string IdentityState => "i" + _identityStateNumber.ToString(CultureInfo.InvariantCulture);

    private (string? Name, JsonObject Args) IdentityGetLive(JsonObject args)
    {
        lock (_lock)
        {
            if (_identityList is null && args["ids"] is null)
            {
                return options.Identities is null ? IdentityGet() : IdentityGetM3();
            }

            var list = IdentityListLocked();
            var ids = (args["ids"] as JsonArray)?.Select(i => (string?)i).ToList();
            var found = list.Where(i => ids is null || ids.Contains((string?)i["id"], StringComparer.Ordinal)).Select(i => (JsonNode?)i.DeepClone()).ToList();
            var missing = (ids ?? []).Where(id => !list.Any(i => string.Equals((string?)i["id"], id, StringComparison.Ordinal))).Select(id => (JsonNode?)id).ToList();
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = IdentityState, ["list"] = new JsonArray([.. found]), ["notFound"] = new JsonArray([.. missing]) });
        }
    }

    // callers hold _lock; the seeds are copied once, in the shape the M3 arm answered (a seeded identity cannot be deleted)
    private List<JsonObject> IdentityListLocked() => _identityList ??=
    [
        .. (options.Identities ?? [new FakeIdentity("i1", "Test User", options.Username)]).Select(i => new JsonObject
        {
            ["id"] = i.Id,
            ["name"] = i.Name,
            ["email"] = i.Email,
            ["replyTo"] = i.ReplyTo is null ? null : new JsonArray(new JsonObject { ["email"] = i.ReplyTo }),
            ["bcc"] = null,
            ["textSignature"] = i.TextSignature,
            ["htmlSignature"] = i.HtmlSignature,
            ["mayDelete"] = false,
        }),
    ];

    private (string? Name, JsonObject Args) IdentitySet(JsonObject args)
    {
        lock (_lock)
        {
            _identitySetRequests.Add((JsonObject)args.DeepClone());
            var failure = _identityFailure;
            _identityFailure = null;
            var list = IdentityListLocked();
            var oldState = IdentityState;
            if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, oldState, StringComparison.Ordinal))
            {
                return Error("stateMismatch");
            }

            JsonObject created = [];
            JsonObject updated = [];
            JsonArray destroyed = [];
            JsonObject notCreated = [];
            JsonObject notUpdated = [];
            JsonObject notDestroyed = [];
            foreach (var (cid, raw) in args["create"] as JsonObject ?? [])
            {
                var item = raw as JsonObject ?? [];
                var bad = failure is not null ? null : InvalidIdentity(item, requireEmail: true);
                if (failure is not null)
                {
                    notCreated[cid] = SetError(failure);
                }
                else if (bad is not null)
                {
                    notCreated[cid] = SetError("invalidProperties", bad);
                }
                else
                {
                    var id = "n" + (++_identityCreated).ToString(CultureInfo.InvariantCulture);
                    var identity = new JsonObject
                    {
                        ["id"] = id,
                        ["name"] = item["name"]?.DeepClone() ?? "",
                        ["email"] = item["email"]!.DeepClone(),
                        ["replyTo"] = item["replyTo"]?.DeepClone(),
                        ["bcc"] = item["bcc"]?.DeepClone(),
                        ["textSignature"] = item["textSignature"]?.DeepClone() ?? "",
                        ["htmlSignature"] = item["htmlSignature"]?.DeepClone() ?? "",
                        ["mayDelete"] = true,
                    };
                    list.Add(identity);
                    created[cid] = new JsonObject { ["id"] = id };
                }
            }

            foreach (var (id, raw) in args["update"] as JsonObject ?? [])
            {
                var target = list.FirstOrDefault(i => string.Equals((string?)i["id"], id, StringComparison.Ordinal));
                var patch = raw as JsonObject ?? [];
                if (failure is not null)
                {
                    notUpdated[id] = SetError(failure);
                }
                else if (target is null)
                {
                    notUpdated[id] = SetError("notFound");
                }
                else if (patch.ContainsKey("email") || patch.ContainsKey("id"))
                {
                    notUpdated[id] = SetError("invalidProperties", patch.ContainsKey("email") ? "email" : "id");
                }
                else if (InvalidIdentity(patch, requireEmail: false) is { } bad)
                {
                    notUpdated[id] = SetError("invalidProperties", bad);
                }
                else
                {
                    foreach (var (key, value) in patch)
                    {
                        target[key] = value?.DeepClone();
                    }

                    updated[id] = null;
                }
            }

            foreach (var id in (args["destroy"] as JsonArray ?? []).Select(i => (string?)i).OfType<string>())
            {
                var target = list.FirstOrDefault(i => string.Equals((string?)i["id"], id, StringComparison.Ordinal));
                if (failure is not null)
                {
                    notDestroyed[id] = SetError(failure);
                }
                else if (target is null)
                {
                    notDestroyed[id] = SetError("notFound");
                }
                else if ((bool?)target["mayDelete"] != true)
                {
                    notDestroyed[id] = SetError("forbidden", "The default identity cannot be deleted.");
                }
                else
                {
                    _ = list.Remove(target);
                    destroyed.Add(id);
                }
            }

            if (created.Count + updated.Count + destroyed.Count > 0)
            {
                _identityStateNumber++;
            }

            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["oldState"] = oldState,
                ["newState"] = IdentityState,
                ["created"] = created.Count == 0 ? null : created,
                ["updated"] = updated.Count == 0 ? null : updated,
                ["destroyed"] = destroyed.Count == 0 ? null : destroyed,
                ["notCreated"] = notCreated.Count == 0 ? null : notCreated,
                ["notUpdated"] = notUpdated.Count == 0 ? null : notUpdated,
                ["notDestroyed"] = notDestroyed.Count == 0 ? null : notDestroyed,
            });
        }
    }

    // the property that makes an identity invalid, or null: Stalwart refuses an address that is not the account's (its own domain, or a domain of one of its identities)
    private string? InvalidIdentity(JsonObject item, bool requireEmail)
    {
        if (!item.ContainsKey("email"))
        {
            return requireEmail ? "email" : null;
        }

        var email = (string?)item["email"] ?? "";
        var at = email.LastIndexOf('@');
        if (at <= 0)
        {
            return "email";
        }

        var domain = email[(at + 1)..];
        var own = IdentityListLocked().Select(i => (string?)i["email"]).Append(options.Username).OfType<string>().Select(e => e[(e.LastIndexOf('@') + 1)..]);
        return own.Contains(domain, StringComparer.OrdinalIgnoreCase) ? null : "email";
    }
}
