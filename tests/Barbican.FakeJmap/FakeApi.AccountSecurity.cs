// The fake server's account-security arms (see FakeAccountSecurity). Answers only with FakeJmapOptions.AccountSecurity (and the Stalwart capability, which the session advertises by default).
// READS are real: x:AccountPassword/get and x:AccountSettings/get answer the "singleton", x:AppPassword and x:ApiKey answer query (all ids) and get. SETS are STUBS (unknownMethod) until set-data fills them: x:AccountPassword/set (a wrong current password is invalidProperties, a new secret is hashed away: never stored or echoed), enabling two-step sign-in (otpAuth), x:AppPassword/set and x:ApiKey/set (create answers the secret once, destroy, expiresAt).
// M6 plan section 4.12. Owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
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
                return Singleton(args, new JsonObject { ["id"] = "singleton", ["description"] = s.Description, ["encryptionAtRest"] = new JsonObject() });
            case ("x:AppPassword", "query"):
                return SecurityQuery(s.AppPasswords);
            case ("x:ApiKey", "query"):
                return SecurityQuery(s.ApiKeys);
            case ("x:AppPassword", "get"):
                return SecurityGet(s.AppPasswords, args);
            case ("x:ApiKey", "get"):
                return SecurityGet(s.ApiKeys, args);
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
}
