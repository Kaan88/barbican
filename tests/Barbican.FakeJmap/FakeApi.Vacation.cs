// The fake server's VacationResponse arms (RFC 8621 section 8; Stalwart crates/jmap/src/vacation/get.rs: get answers the singleton "singleton" whatever ids; set.rs: update only, a toDate before the fromDate is invalidProperties, a create or destroy is forbidden). Answers only with FakeJmapOptions.VacationCapability.
// get is REAL; set is a STUB that answers unknownMethod until srv-data fills it (update isEnabled, fromDate, toDate, subject, textBody, htmlBody with ifInState, state strings, FailNextSet like FakeApi.CalendarSet.cs). M6 plan section 4.12. Owner srv-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) VacationGet(JsonObject args)
    {
        if (!options.VacationCapability)
        {
            return Error("unknownMethod");
        }

        var v = options.Vacation ?? new FakeVacation();
        var ids = args["ids"] as JsonArray;
        var wanted = ids is null || ids.Any(i => string.Equals((string?)i, "singleton", StringComparison.Ordinal));
        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["state"] = "v1",
            ["list"] = wanted
                ? new JsonArray(new JsonObject
                {
                    ["id"] = "singleton",
                    ["isEnabled"] = v.IsEnabled,
                    ["fromDate"] = v.FromDate,
                    ["toDate"] = v.ToDate,
                    ["subject"] = v.Subject,
                    ["textBody"] = v.TextBody,
                    ["htmlBody"] = v.HtmlBody,
                })
                : new JsonArray(),
            ["notFound"] = new JsonArray([.. (ids ?? []).Where(i => !string.Equals((string?)i, "singleton", StringComparison.Ordinal)).Select(i => (JsonNode?)(string?)i)]),
        });
    }

    private static (string? Name, JsonObject Args) VacationSet(JsonObject args)
    {
        _ = args;
        return Error("unknownMethod");
    }
}
