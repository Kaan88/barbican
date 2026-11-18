// The fake server's VacationResponse arms (RFC 8621 section 8; Stalwart crates/jmap/src/vacation/get.rs: get answers the singleton "singleton" whatever ids; crates/jmap/src/vacation/set.rs: update only, a toDate before the fromDate is invalidProperties, a create or destroy is "singleton");
// Bulwark lib/jmap/client.ts:5310-5347 (getVacationResponse, setVacationResponse). Answers only with FakeJmapOptions.VacationCapability. The singleton is mutable: set takes isEnabled, fromDate, toDate, subject, textBody, htmlBody (null clears), honours ifInState with the method error stateMismatch (RFC 8620 5.3),
// moves the state string v1, v2, ... on a change, logs the requests (VacationSetRequests) and fails the next write on demand (FailNextVacationSet) as FakeApi.CalendarSet.cs does. M6 plan section 4.12. Owner srv-data.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static readonly string[] VacationProperties = ["isEnabled", "fromDate", "toDate", "subject", "textBody", "htmlBody"];

    private readonly List<JsonObject> _vacationSetRequests = [];
    private readonly List<string> _vacationFailures = [];
    private FakeVacation? _vacation;
    private int _vacationState = 1;

    /// <summary>Every VacationResponse/set that arrived, with its arguments (a copy).</summary>
    public IReadOnlyList<JsonObject> VacationSetRequests
    {
        get
        {
            lock (_lock)
            {
                return [.. _vacationSetRequests];
            }
        }
    }

    /// <summary>The vacation response as the server has it now.</summary>
    public FakeVacation CurrentVacation
    {
        get
        {
            lock (_lock)
            {
                return _vacation ?? options.Vacation ?? new FakeVacation();
            }
        }
    }

    /// <summary>The next VacationResponse/set refuses its update with this SetError type (for instance "forbidden" or "serverFail"); nothing changes.</summary>
    public void FailNextVacationSet(string errorType)
    {
        lock (_lock)
        {
            _vacationFailures.Add(errorType);
        }
    }

    private (string? Name, JsonObject Args) VacationGet(JsonObject args)
    {
        if (!options.VacationCapability)
        {
            return Error("unknownMethod");
        }

        var v = CurrentVacation;
        var ids = args["ids"] as JsonArray;
        var wanted = ids is null || ids.Any(i => string.Equals((string?)i, "singleton", StringComparison.Ordinal));
        string state;
        lock (_lock)
        {
            state = VacationStateText();
        }

        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["state"] = state,
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

    private (string? Name, JsonObject Args) VacationSet(JsonObject args)
    {
        if (!options.VacationCapability)
        {
            return Error("unknownMethod");
        }

        lock (_lock)
        {
            _vacationSetRequests.Add((JsonObject)args.DeepClone());
            var old = VacationStateText();
            if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, old, StringComparison.Ordinal))
            {
                return Error("stateMismatch");
            }

            var failure = _vacationFailures.Count > 0 ? _vacationFailures[0] : null;
            if (failure is not null)
            {
                _vacationFailures.RemoveAt(0);
            }

            var updated = new JsonObject();
            var notUpdated = new JsonObject();
            foreach (var (id, patch) in args["update"] as JsonObject ?? [])
            {
                var err = failure ?? (string.Equals(id, "singleton", StringComparison.Ordinal) ? ApplyVacation(patch as JsonObject) : "notFound");
                if (err is null)
                {
                    updated[id] = null;
                }
                else
                {
                    notUpdated[id] = new JsonObject { ["type"] = err.StartsWith("invalid", StringComparison.Ordinal) ? "invalidProperties" : err };
                    if (err.StartsWith("invalid:", StringComparison.Ordinal))
                    {
                        notUpdated[id]!["properties"] = new JsonArray(err["invalid:".Length..]);
                    }
                }
            }

            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["oldState"] = old,
                ["newState"] = VacationStateText(),
                ["created"] = null,
                ["notCreated"] = new JsonObject(((args["create"] as JsonObject) ?? []).Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, new JsonObject { ["type"] = "singleton" }))),
                ["updated"] = updated,
                ["notUpdated"] = notUpdated,
                ["destroyed"] = null,
                ["notDestroyed"] = new JsonObject(((args["destroy"] as JsonArray) ?? []).Select(x => new KeyValuePair<string, JsonNode?>((string?)x ?? string.Empty, new JsonObject { ["type"] = "singleton" }))),
            });
        }
    }

    // inside the lock; the SetError type, "invalid:<property>" for invalidProperties with that property, or null
    private string? ApplyVacation(JsonObject? patch)
    {
        if (patch is null)
        {
            return "invalidPatch";
        }

        var bad = patch.Select(kv => kv.Key).FirstOrDefault(k => !VacationProperties.Contains(k, StringComparer.Ordinal));
        if (bad is not null)
        {
            return "invalid:" + bad;
        }

        var v = _vacation ?? options.Vacation ?? new FakeVacation();
        foreach (var (key, value) in patch)
        {
            var wrongType = string.Equals(key, "isEnabled", StringComparison.Ordinal) ? value is not JsonValue b || !b.TryGetValue<bool>(out _) : value is not null && (value is not JsonValue s || !s.TryGetValue<string>(out _));
            if (wrongType)
            {
                return "invalid:" + key;
            }

            v = key switch
            {
                "isEnabled" => v with { IsEnabled = (bool)value! },
                "fromDate" => v with { FromDate = (string?)value },
                "toDate" => v with { ToDate = (string?)value },
                "subject" => v with { Subject = (string?)value },
                "textBody" => v with { TextBody = (string?)value },
                _ => v with { HtmlBody = (string?)value },
            };
        }

        if (v.FromDate is not null && v.ToDate is not null
            && DateTimeOffset.TryParse(v.FromDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from)
            && DateTimeOffset.TryParse(v.ToDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var to)
            && to < from)
        {
            return "invalid:toDate";
        }

        _vacation = v;
        _vacationState++;
        return null;
    }

    private string VacationStateText() => "v" + _vacationState.ToString(CultureInfo.InvariantCulture);
}
