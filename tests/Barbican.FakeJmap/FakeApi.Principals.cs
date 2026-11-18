// The fake server's directory (JMAP Principals RFC 9670; Stalwart crates/jmap/src/principal/): Principal/query over FakeJmapOptions.Principals (paged by position and limit, capped by QueryLimitCap; an email filter; forbidden when
// PrincipalQueryForbidden) and Principal/get (ids, properties; without ids every principal, which is what a client falls back to when the query is refused). The calendar's Principal/query (availability) stays in FakeApi.Calendar.cs. M5 plan section 4.13. Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) PrincipalDirectoryQuery(JsonObject args)
    {
        if (options.Principals is not { } all || !(options.PrincipalsCapability || options.AvailabilityCapability))
        {
            return Error("unknownMethod");
        }

        if (options.PrincipalQueryForbidden)
        {
            return Error("forbidden");
        }

        var email = ((string?)(args["filter"] as JsonObject)?["email"])?.ToLowerInvariant();
        var ids = all.Where(p => email is null || string.Equals(((string?)p["email"])?.ToLowerInvariant(), email, StringComparison.Ordinal)).Select(p => (string)p["id"]!).ToList();
        var position = args["position"] is JsonValue pv ? pv.GetValue<int>() : 0;
        var limit = Math.Min(args["limit"] is JsonValue lv ? lv.GetValue<int>() : options.QueryLimitCap, options.QueryLimitCap);
        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["queryState"] = "P1",
            ["canCalculateChanges"] = false,
            ["position"] = position,
            ["total"] = ids.Count,
            ["ids"] = new JsonArray([.. ids.Skip(position).Take(limit).Select(i => (JsonNode)i)]),
        });
    }

    private (string? Name, JsonObject Args) PrincipalGet(JsonObject args)
    {
        if (options.Principals is not { } all || !(options.PrincipalsCapability || options.AvailabilityCapability))
        {
            return Error("unknownMethod");
        }

        var wanted = args["ids"] is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToList() : null;
        var properties = args["properties"] as JsonArray;
        var list = new JsonArray();
        var notFound = new JsonArray();
        foreach (var p in wanted is null ? all : wanted.Select(i => all.FirstOrDefault(x => string.Equals((string?)x["id"], i, StringComparison.Ordinal))).OfType<JsonObject>())
        {
            list.Add(Pick(p, properties));
        }

        foreach (var i in wanted?.Where(i => !all.Any(x => string.Equals((string?)x["id"], i, StringComparison.Ordinal))) ?? [])
        {
            notFound.Add(i);
        }

        return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = "P1", ["list"] = list, ["notFound"] = notFound });
    }
}
