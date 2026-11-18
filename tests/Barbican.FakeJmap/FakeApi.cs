// The fake server's API endpoint: RFC 8620 §3.3-§3.6 (request, response, request-level problems: unknownCapability, notRequest,
// limit maxCallsInRequest / maxSizeRequest), §3.6.2 (method errors: unknownMethod, invalidArguments, invalidResultReference,
// accountNotFound), §3.7 (result references); Stalwart crates/jmap/src/api/request.rs. Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>Runs JMAP requests against a <see cref="FakeStore"/>.</summary>
public sealed partial class FakeApi(FakeStore store, FakeJmapOptions options, Func<string> sessionState)
{
    private static readonly string[] Known =
        ["urn:ietf:params:jmap:core", "urn:ietf:params:jmap:mail", "urn:ietf:params:jmap:submission", "urn:ietf:params:jmap:quota", "urn:stalwart:jmap",
        "urn:ietf:params:jmap:sieve", "urn:ietf:params:jmap:filenode", "urn:ietf:params:jmap:calendars", "urn:ietf:params:jmap:metadata", "urn:ietf:params:jmap:principals", "urn:ietf:params:jmap:principals:availability"];

    private readonly Queue<FakeFault> _methodFaults = new();
    private readonly Lock _lock = new();

    /// <summary>Queues a /changes or /queryChanges fault.</summary>
    public void Inject(FakeFault fault)
    {
        lock (_lock)
        {
            _methodFaults.Enqueue(fault);
        }
    }

    /// <summary>The response object, or an RFC 7807 problem (status, body).</summary>
    public (int Status, JsonObject Body) Run(JsonObject request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request["using"] is not JsonArray use || request["methodCalls"] is not JsonArray calls)
        {
            return Problem(400, "urn:ietf:params:jmap:error:notRequest", "The request is not a JMAP request.", null);
        }

        if (use.Select(u => (string?)u).FirstOrDefault(u => !Known.Contains(u, StringComparer.Ordinal)) is { } unknown)
        {
            return Problem(400, "urn:ietf:params:jmap:error:unknownCapability", $"Unknown capability {unknown}.", null);
        }

        if (calls.Count > options.MaxCallsInRequest)
        {
            return Problem(400, "urn:ietf:params:jmap:error:limit", "Too many calls.", "maxCallsInRequest");
        }

        var responses = new List<JsonArray>();
        ForgetCreations();
        foreach (var call in calls.OfType<JsonArray>())
        {
            var name = (string?)call[0] ?? "";
            var id = (string?)call[2] ?? "";
            var args = call[1] is JsonObject a ? ResultReferences.Resolve(a, responses) : null;
            var result = args is null ? Error("invalidResultReference") : Method(name, args);
            responses.Add([result.Name ?? name, result.Args, id]);
            RememberCreations(result.Args);
        }

        return (200, new JsonObject { ["methodResponses"] = new JsonArray([.. responses]), ["sessionState"] = sessionState() });
    }

    /// <summary>An RFC 7807 problem body.</summary>
    public static (int Status, JsonObject Body) Problem(int status, string type, string detail, string? limit)
    {
        var o = new JsonObject { ["type"] = type, ["status"] = status, ["detail"] = detail };
        if (limit is not null)
        {
            o["limit"] = limit;
        }

        return (status, o);
    }

    private static (string? Name, JsonObject Args) Error(string type, string? description = null) =>
        ("error", description is null ? new JsonObject { ["type"] = type } : new JsonObject { ["type"] = type, ["description"] = description });

    private (string? Name, JsonObject Args) Method(string name, JsonObject args)
    {
        if (!string.Equals(name, "Core/echo", StringComparison.Ordinal) && !string.Equals((string?)args["accountId"], options.AccountId, StringComparison.Ordinal))
        {
            return Error("accountNotFound");
        }

        return name switch
        {
            "Core/echo" => (null, args),
            "Mailbox/get" => MailboxGet(args),
            "Mailbox/changes" => Changes("Mailbox", args),
            "Mailbox/query" => MailboxQuery(args),
            "Email/get" => EmailGet(args),
            "Email/query" => EmailQuery(args),
            "Email/changes" => TakeFault(FakeFault.EmailChangesFail) ? Error("cannotCalculateChanges") : Changes("Email", args),
            "Email/queryChanges" => EmailQueryChanges(args),
            "Email/set" => EmailSetWithCreate(args),
            "Thread/get" => ThreadGet(args),
            "Thread/changes" => Changes("Thread", args),
            "Identity/get" => options.Identities is null ? IdentityGet() : IdentityGetM3(),
            "Quota/get" => QuotaGet(),

            // M2 arms (stage F): each calls a method in its own file that the owning stream fills (M2 plan 3.1, 4); until then the method answers unknownMethod
            "Mailbox/set" => MailboxSetWithMetadata(args),
            "Email/import" => EmailImport(args),
            "Email/copy" => EmailCopy(args),
            "Email/parse" => EmailParse(args),
            "Blob/copy" => BlobCopy(args),
            "SearchSnippet/get" => SearchSnippetGet(args),
            "EmailSubmission/set" => EmailSubmissionSetHeld(args),
            "EmailSubmission/get" => EmailSubmissionGet(args),
            "EmailSubmission/query" => EmailSubmissionQuery(args),
            "SieveScript/get" => SieveScriptGet(args),
            "SieveScript/set" => SieveScriptSet(args),
            "SieveScript/validate" => SieveScriptValidate(args),
            "FileNode/get" => FileNodeGet(args),
            "FileNode/query" => FileNodeQuery(args),
            "FileNode/set" => FileNodeSet(args),
            "CalendarEvent/parse" => CalendarEventParse(args),

            // M4 arms (plan section 4.7): reads and changes are cal-data's (FakeApi.Calendar.cs), writes are cal-edit's (FakeApi.CalendarSet.cs); each answers unknownMethod without options.Calendars
            "Calendar/get" => CalendarGet("Calendar", args),
            "Calendar/changes" => CalendarChanges("Calendar", args),
            "Calendar/set" => CalendarSet("Calendar", args),
            "CalendarEvent/get" => CalendarGet("CalendarEvent", args),
            "CalendarEvent/changes" => CalendarChanges("CalendarEvent", args),
            "CalendarEvent/query" => CalendarEventQuery(args),
            "CalendarEvent/set" => CalendarSet("CalendarEvent", args),
            "Principal/query" => PrincipalQuery(args),
            "Principal/getAvailability" => PrincipalGetAvailability(args),
            "x:ArchivedItem/get" => ArchivedItemGet(args),
            "x:ArchivedItem/query" => ArchivedItemQuery(args),
            "x:ArchivedItem/set" => ArchivedItemSet(args),
            _ => Error("unknownMethod"),
        };
    }

    private bool TakeFault(FakeFault fault)
    {
        lock (_lock)
        {
            if (_methodFaults.Count > 0 && _methodFaults.Peek() == fault)
            {
                _methodFaults.Dequeue();
                return true;
            }

            return false;
        }
    }

    private (string? Name, JsonObject Args) Changes(string type, JsonObject args)
    {
        var since = (string?)args["sinceState"] ?? "";
        var max = args["maxChanges"] is JsonValue m ? (int?)m.GetValue<int>() : null;
        if (max is <= 0)
        {
            return Error("invalidArguments", "maxChanges must be positive");
        }

        return store.Changes(type, since, max) is { } c ? (null, c.ToJson(options.AccountId, string.Equals(type, "Mailbox", StringComparison.Ordinal))) : Error("cannotCalculateChanges");
    }

    private (string? Name, JsonObject Args) IdentityGet() => (null, new JsonObject
    {
        ["accountId"] = options.AccountId,
        ["state"] = "i1",
        ["list"] = new JsonArray(new JsonObject
        {
            ["id"] = "i1",
            ["name"] = "Test User",
            ["email"] = options.Username,
            ["replyTo"] = null,
            ["bcc"] = null,
            ["textSignature"] = "",
            ["htmlSignature"] = "",
            ["mayDelete"] = false,
        }),
        ["notFound"] = new JsonArray(),
    });

    private (string? Name, JsonObject Args) QuotaGet() => (null, new JsonObject
    {
        ["accountId"] = options.AccountId,
        ["state"] = "q1",
        ["list"] = new JsonArray(new JsonObject
        {
            ["id"] = "q1",
            ["resourceType"] = "octets",
            ["used"] = 3_500_000,
            ["hardLimit"] = 1_000_000_000,
            ["scope"] = "account",
            ["name"] = options.Username,
            ["types"] = new JsonArray("Mail"),
        }),
        ["notFound"] = new JsonArray(),
    });
}
