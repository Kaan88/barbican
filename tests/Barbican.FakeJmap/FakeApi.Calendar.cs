// The fake server's calendar methods (JMAP Calendars draft; Stalwart crates/jmap/src/calendar/{get,set,changes}.rs, calendar_event/{query,get,changes}.rs, principal/getAvailability): Calendar/get, Calendar/changes, CalendarEvent/query (filters inCalendars, uid, after,
// before), CalendarEvent/get, CalendarEvent/changes and Principal/query and getAvailability (busy time from the events a guest takes part in). The set methods are cal-edit's (FakeApi.CalendarSet.cs). Answers only with FakeJmapOptions.Calendars. M4 plan section 4.7. Owner cal-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) CalendarGet(string type, JsonObject args)
    {
        if (CalendarsOf(args) is not { } cals || !options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        if (cals.Fails)
        {
            return Error("serverFail");
        }

        var all = string.Equals(type, "Calendar", StringComparison.Ordinal) ? cals.Calendars() : cals.Events();
        var ids = args["ids"] is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToList() : null;
        var max = options.MaxObjectsInGet;
        if (ids is not null && ids.Count > max)
        {
            return Error("requestTooLarge");
        }

        var list = new JsonArray();
        var notFound = new JsonArray();
        foreach (var o in ids is null ? all : ids.Select(i => all.FirstOrDefault(x => string.Equals((string?)x["id"], i, StringComparison.Ordinal))))
        {
            if (o is null)
            {
                continue;
            }

            list.Add(Pick(o, args["properties"] as JsonArray));
        }

        if (ids is not null)
        {
            foreach (var i in ids.Where(i => !all.Any(x => string.Equals((string?)x["id"], i, StringComparison.Ordinal))))
            {
                notFound.Add(i);
            }
        }

        return (null, new JsonObject { ["accountId"] = (string?)args["accountId"] ?? options.AccountId, ["state"] = cals.State(type), ["list"] = list, ["notFound"] = notFound });
    }

    /// <summary>The calendar data an account id names: a shared account's (<see cref="FakeJmapOptions.SharedCalendars"/>) or the user's own.</summary>
    private FakeCalendars? CalendarsOf(JsonObject args) => (string?)args["accountId"] is { } id && options.SharedCalendars.TryGetValue(id, out var shared) ? shared : options.Calendars;

    private static JsonObject Pick(JsonObject o, JsonArray? properties)
    {
        if (properties is null)
        {
            return o;
        }

        var keep = properties.Select(p => (string?)p).OfType<string>().Append("id").ToHashSet(StringComparer.Ordinal);
        var r = new JsonObject();
        foreach (var (k, v) in o.Where(kv => keep.Contains(kv.Key)))
        {
            r[k] = v?.DeepClone();
        }

        return r;
    }

    private (string? Name, JsonObject Args) CalendarChanges(string type, JsonObject args)
    {
        if (CalendarsOf(args) is not { } cals || !options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        if (cals.Fails)
        {
            return Error("serverFail");
        }

        var since = (string?)args["sinceState"] ?? "";
        if (cals.ChangesSince(type, since) is not { } c)
        {
            return Error("cannotCalculateChanges");
        }

        return (null, new JsonObject
        {
            ["accountId"] = (string?)args["accountId"] ?? options.AccountId,
            ["oldState"] = since,
            ["newState"] = cals.State(type),
            ["hasMoreChanges"] = false,
            ["created"] = new JsonArray([.. c.Created.Select(i => (JsonNode)i)]),
            ["updated"] = new JsonArray([.. c.Updated.Select(i => (JsonNode)i)]),
            ["destroyed"] = new JsonArray([.. c.Destroyed.Select(i => (JsonNode)i)]),
        });
    }

    private (string? Name, JsonObject Args) CalendarEventQuery(JsonObject args)
    {
        if (CalendarsOf(args) is not { } cals || !options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        if (cals.Fails)
        {
            return Error("serverFail");
        }

        var filter = args["filter"] as JsonObject;
        var inCals = filter?["inCalendars"] is JsonArray ic ? ic.Select(x => (string?)x).OfType<string>().ToHashSet(StringComparer.Ordinal) : null;
        var uid = (string?)filter?["uid"];
        var ids = cals.Events()
            .Where(e => uid is null || string.Equals((string?)e["uid"], uid, StringComparison.Ordinal))
            .Where(e => inCals is null || (e["calendarIds"] as JsonObject ?? []).Any(k => inCals.Contains(k.Key)))
            .Select(e => (string)e["id"]!)
            .ToList();
        var position = args["position"] is JsonValue p ? p.GetValue<int>() : 0;
        var limit = Math.Min(args["limit"] is JsonValue l ? l.GetValue<int>() : options.QueryLimitCap, options.QueryLimitCap);
        return (null, new JsonObject
        {
            ["accountId"] = (string?)args["accountId"] ?? options.AccountId,
            ["queryState"] = cals.State("CalendarEvent"),
            ["canCalculateChanges"] = false,
            ["position"] = position,
            ["total"] = ids.Count,
            ["ids"] = new JsonArray([.. ids.Skip(position).Take(limit).Select(i => (JsonNode)i)]),
        });
    }

    private (string? Name, JsonObject Args) PrincipalQuery(JsonObject args)
    {
        if (options.Calendars is null || !options.AvailabilityCapability)
        {
            return Error("unknownMethod");
        }

        var email = (string?)(args["filter"] as JsonObject)?["email"];
        return (null, new JsonObject { ["accountId"] = options.AccountId, ["ids"] = email is null ? new JsonArray() : new JsonArray("p:" + email.ToLowerInvariant()), ["total"] = email is null ? 0 : 1 });
    }

    private (string? Name, JsonObject Args) PrincipalGetAvailability(JsonObject args)
    {
        if (options.Calendars is not { } cals || !options.AvailabilityCapability)
        {
            return Error("unknownMethod");
        }

        var email = ((string?)args["id"] ?? "").Replace("p:", string.Empty, StringComparison.Ordinal);
        var from = DateTimeOffset.Parse((string?)args["utcStart"] ?? "1970-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse((string?)args["utcEnd"] ?? "9999-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var list = new JsonArray();
        foreach (var e in cals.Events().Where(e => (e["participants"] as JsonObject ?? []).Any(p => string.Equals(((string?)p.Value?["email"] ?? (string?)p.Value?["calendarAddress"])?.Replace("mailto:", string.Empty, StringComparison.Ordinal), email, StringComparison.OrdinalIgnoreCase))))
        {
            if (e["utcStart"] is not JsonValue us || e["utcEnd"] is not JsonValue ue)
            {
                continue;
            }

            var s = DateTimeOffset.Parse((string)us!, System.Globalization.CultureInfo.InvariantCulture);
            var f = DateTimeOffset.Parse((string)ue!, System.Globalization.CultureInfo.InvariantCulture);
            if (s < to && f > from)
            {
                list.Add(new JsonObject { ["utcStart"] = (string)us!, ["utcEnd"] = (string)ue!, ["busyStatus"] = (string?)e["freeBusyStatus"] is "tentative" ? "tentative" : "confirmed" });
            }
        }

        return (null, new JsonObject { ["accountId"] = options.AccountId, ["list"] = list });
    }
}
