// The fake server's account-security arms (see FakeAccountSecurity). Answers only with FakeJmapOptions.AccountSecurity (and the Stalwart capability, which the session advertises by default).
// READS: x:AccountPassword/get and x:AccountSettings/get answer the "singleton", x:AppPassword and x:ApiKey answer query (all ids) and get. SETS: x:AccountPassword/set (a wrong currentSecret is invalidProperties; a new secret is hashed away: never stored or echoed; otpAuth with an otpUrl and a six-digit otpCode turns two-step sign-in on, otpUrl null turns it off),
// x:AccountSettings/set (description, encryptionAtRest), x:PublicKey/set (create), x:AppPassword/set and x:ApiKey/set (create answers the secret once and lists it never again; destroy). The requests are logged with every secret replaced by "***" (SecuritySetRequests); FailNextSecuritySet refuses the next write.
// M6 plan section 4.12. Owner set-data.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<(string Type, JsonObject Args)> _securitySetRequests = [];
    private string? _securityFailure;
    private int _securityCreated;

    /// <summary>Every management /set that arrived, with its arguments (a copy; passwords, secrets and codes are replaced by "***").</summary>
    public IReadOnlyList<(string Type, JsonObject Args)> SecuritySetRequests
    {
        get
        {
            lock (_lock)
            {
                return [.. _securitySetRequests.Select(r => (r.Type, (JsonObject)r.Args.DeepClone()))];
            }
        }
    }

    /// <summary>The next management write is refused object by object with this SetError type; nothing changes.</summary>
    public void FailNextSecuritySet(string errorType)
    {
        lock (_lock)
        {
            _securityFailure = errorType;
        }
    }

    private (string? Name, JsonObject Args) AccountSecurityMethod(string name, JsonObject args)
    {
        if (options.AccountSecurity is not { } s)
        {
            return Error("unknownMethod");
        }

        var type = name[..name.IndexOf('/', StringComparison.Ordinal)];
        var op = name[(name.IndexOf('/', StringComparison.Ordinal) + 1)..];
        switch (type, op)
        {
            case ("x:AccountPassword", "get"):
                return Singleton(args, new JsonObject { ["id"] = "singleton", ["otpAuth"] = s.OtpUrl is null ? null : new JsonObject { ["otpUrl"] = s.OtpUrl } });
            case ("x:AccountSettings", "get"):
                return Singleton(args, new JsonObject { ["id"] = "singleton", ["description"] = s.Description, ["encryptionAtRest"] = new JsonObject { ["@type"] = s.EncryptionType } });
            case ("x:AppPassword", "query"):
                return SecurityQuery(s.AppPasswords);
            case ("x:ApiKey", "query"):
                return SecurityQuery(s.ApiKeys);
            case ("x:AppPassword", "get"):
                return SecurityGet(s.AppPasswords, args);
            case ("x:ApiKey", "get"):
                return SecurityGet(s.ApiKeys, args);
            case (_, "set"):
                return SecuritySet(s, type, args);
            default:
                return Error("unknownMethod");
        }
    }

    private (string? Name, JsonObject Args) Singleton(JsonObject args, JsonObject item)
    {
        _ = args;
        return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = "x1", ["list"] = new JsonArray(item), ["notFound"] = new JsonArray() });
    }

    private (string? Name, JsonObject Args) SecurityQuery(IList<JsonObject> items)
    {
        lock (_lock)
        {
            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["queryState"] = "x1",
                ["canCalculateChanges"] = false,
                ["position"] = 0,
                ["total"] = items.Count,
                ["ids"] = new JsonArray([.. items.Select(i => (JsonNode?)(string?)i["id"])]),
            });
        }
    }

    private (string? Name, JsonObject Args) SecurityGet(IList<JsonObject> items, JsonObject args)
    {
        lock (_lock)
        {
            var ids = (args["ids"] as JsonArray)?.Select(i => (string?)i).ToList();
            var found = items.Where(i => ids is null || ids.Contains((string?)i["id"], StringComparer.Ordinal)).Select(i => (JsonNode?)i.DeepClone()).ToList();
            var missing = (ids ?? []).Where(id => !items.Any(i => string.Equals((string?)i["id"], id, StringComparison.Ordinal))).Select(id => (JsonNode?)id).ToList();
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = "x1", ["list"] = new JsonArray([.. found]), ["notFound"] = new JsonArray([.. missing]) });
        }
    }

    private static JsonObject Redacted(JsonObject args)
    {
        var copy = (JsonObject)args.DeepClone();
        foreach (var node in copy["update"] is JsonObject u ? u.Select(kv => kv.Value).OfType<JsonObject>() : [])
        {
            foreach (var key in new[] { "currentSecret", "secret" })
            {
                if (node.ContainsKey(key))
                {
                    node[key] = "***";
                }
            }

            if (node["otpAuth"] is JsonObject otp && otp.ContainsKey("otpCode"))
            {
                otp["otpCode"] = "***";
            }
        }

        return copy;
    }

    private (string? Name, JsonObject Args) SecuritySet(FakeAccountSecurity s, string type, JsonObject args)
    {
        lock (_lock)
        {
            _securitySetRequests.Add((type, Redacted(args)));
            var failure = _securityFailure;
            _securityFailure = null;
            JsonObject created = [];
            JsonObject updated = [];
            JsonArray destroyed = [];
            JsonObject notCreated = [];
            JsonObject notUpdated = [];
            JsonObject notDestroyed = [];
            foreach (var (cid, raw) in args["create"] as JsonObject ?? [])
            {
                var item = raw as JsonObject ?? [];
                if (failure is not null)
                {
                    notCreated[cid] = SetError(failure);
                }
                else if (SecurityList(s, type) is not { } list || string.IsNullOrWhiteSpace((string?)item["description"]) && !string.Equals(type, "x:PublicKey", StringComparison.Ordinal))
                {
                    notCreated[cid] = SetError("invalidProperties", "description");
                }
                else
                {
                    var n = (++_securityCreated).ToString(CultureInfo.InvariantCulture);
                    var id = (string.Equals(type, "x:AppPassword", StringComparison.Ordinal) ? "ap" : string.Equals(type, "x:ApiKey", StringComparison.Ordinal) ? "ak" : "pk") + n;
                    var stored = new JsonObject { ["id"] = id, ["description"] = item["description"]?.DeepClone(), ["createdAt"] = "2026-01-01T00:00:00Z", ["expiresAt"] = item["expiresAt"]?.DeepClone() };
                    if (string.Equals(type, "x:PublicKey", StringComparison.Ordinal))
                    {
                        stored["key"] = item["key"]?.DeepClone();
                    }

                    list.Add(stored);
                    var answer = new JsonObject { ["id"] = id, ["createdAt"] = "2026-01-01T00:00:00Z" };
                    if (!string.Equals(type, "x:PublicKey", StringComparison.Ordinal))
                    {
                        // the secret is issued once and never listed again (a fixed fake value: nothing real)
                        answer["secret"] = "fake-secret-" + n;
                    }

                    created[cid] = answer;
                }
            }

            foreach (var (id, raw) in args["update"] as JsonObject ?? [])
            {
                var patch = raw as JsonObject ?? [];
                var problem = failure is not null ? SetError(failure) : ApplySingleton(s, type, id, patch);
                if (problem is null)
                {
                    updated[id] = null;
                }
                else
                {
                    notUpdated[id] = problem;
                }
            }

            foreach (var id in (args["destroy"] as JsonArray ?? []).Select(i => (string?)i).OfType<string>())
            {
                var list = SecurityList(s, type);
                var target = list?.FirstOrDefault(i => string.Equals((string?)i["id"], id, StringComparison.Ordinal));
                if (failure is not null)
                {
                    notDestroyed[id] = SetError(failure);
                }
                else if (list is null || target is null)
                {
                    notDestroyed[id] = SetError("notFound");
                }
                else
                {
                    _ = list.Remove(target);
                    destroyed.Add(id);
                }
            }

            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["oldState"] = "x1",
                ["newState"] = "x1",
                ["created"] = created.Count == 0 ? null : created,
                ["updated"] = updated.Count == 0 ? null : updated,
                ["destroyed"] = destroyed.Count == 0 ? null : destroyed,
                ["notCreated"] = notCreated.Count == 0 ? null : notCreated,
                ["notUpdated"] = notUpdated.Count == 0 ? null : notUpdated,
                ["notDestroyed"] = notDestroyed.Count == 0 ? null : notDestroyed,
            });
        }
    }

    private static IList<JsonObject>? SecurityList(FakeAccountSecurity s, string type) => type switch
    {
        "x:AppPassword" => s.AppPasswords,
        "x:ApiKey" => s.ApiKeys,
        "x:PublicKey" => s.PublicKeys,
        _ => null,
    };

    private static JsonObject? ApplySingleton(FakeAccountSecurity s, string type, string id, JsonObject patch)
    {
        if (!string.Equals(id, "singleton", StringComparison.Ordinal))
        {
            return SetError("notFound");
        }

        if (string.Equals(type, "x:AccountSettings", StringComparison.Ordinal))
        {
            if (patch["description"] is { } d)
            {
                s.Description = (string?)d ?? string.Empty;
            }

            if (patch["encryptionAtRest"] is JsonObject enc)
            {
                var kind = (string?)enc["@type"] ?? "Disabled";
                if (!string.Equals(kind, "Disabled", StringComparison.Ordinal) && !s.PublicKeys.Any(k => string.Equals((string?)k["id"], (string?)enc["publicKey"], StringComparison.Ordinal)))
                {
                    return SetError("invalidProperties", "encryptionAtRest: publicKey");
                }

                s.EncryptionType = kind;
            }

            return null;
        }

        if (!string.Equals(type, "x:AccountPassword", StringComparison.Ordinal))
        {
            return SetError("invalidProperties", "type");
        }

        if (!s.IsPassword((string?)patch["currentSecret"]))
        {
            return SetError("invalidProperties", "The current password is not correct.");
        }

        if (patch["otpAuth"] is JsonObject otp)
        {
            if (otp["otpUrl"] is null)
            {
                s.OtpUrl = null;
            }
            else
            {
                var code = ((string?)otp["otpCode"] ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
                if (code.Length != 6 || !code.All(char.IsAsciiDigit))
                {
                    return SetError("invalidProperties", "The code is not correct.");
                }

                s.OtpUrl = (string?)otp["otpUrl"];
            }
        }

        if ((string?)patch["secret"] is { } secret)
        {
            if (secret.Length < 8)
            {
                return SetError("invalidProperties", "The new password is too short.");
            }

            s.SetPassword(secret);
        }

        return null;
    }
}
