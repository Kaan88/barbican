// The fake server's resources: the Session object (RFC 8620 §2: capabilities with the core limits, accounts with account-level
// capabilities, primaryAccounts, URL templates, state), the API endpoint (§3.1, maxSizeRequest §3.6.1 limit problem), downloads and
// uploads (§6.1, §6.2), the event source (§7.3) and OAuth (RFC 8414 §3, RFC 6749 §6). Owner jmap.

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeJmapServer
{
    private JsonObject Session(string path)
    {
        _ = path;
        var o = Options;
        string Url(string p) => o.RelativeUrls ? p : (o.SessionUrlOrigin ?? o.Origin) + p;
        var core = new JsonObject
        {
            ["maxSizeUpload"] = o.MaxSizeUpload,
            ["maxConcurrentUpload"] = o.MaxConcurrentUpload,
            ["maxSizeRequest"] = o.MaxSizeRequest,
            ["maxConcurrentRequests"] = o.MaxConcurrentRequests,
            ["maxCallsInRequest"] = o.MaxCallsInRequest,
            ["maxObjectsInGet"] = o.MaxObjectsInGet,
            ["maxObjectsInSet"] = o.MaxObjectsInSet,
            ["collationAlgorithms"] = new JsonArray("i;ascii-numeric", "i;ascii-casemap", "i;unicode-casemap"),
        };
        var caps = new JsonObject
        {
            ["urn:ietf:params:jmap:core"] = core,
            ["urn:ietf:params:jmap:mail"] = new JsonObject(),
            ["urn:ietf:params:jmap:submission"] = new JsonObject(),
            ["urn:ietf:params:jmap:quota"] = new JsonObject(),
        };
        if (o.Stalwart)
        {
            caps["urn:stalwart:jmap"] = new JsonObject();
        }

        AddM2Capabilities(caps, o);

        var accountCaps = new JsonObject
        {
            ["urn:ietf:params:jmap:mail"] = new JsonObject { ["maxMailboxesPerEmail"] = null, ["maxMailboxDepth"] = 10, ["emailQuerySortOptions"] = new JsonArray("receivedAt") },
            ["urn:ietf:params:jmap:submission"] = new JsonObject { ["maxDelayedSend"] = o.MaxDelayedSend, ["submissionExtensions"] = new JsonObject() },
            ["urn:ietf:params:jmap:quota"] = new JsonObject(),
        };
        var session = new JsonObject
        {
            ["capabilities"] = caps,
            ["accounts"] = new JsonObject { [o.AccountId] = new JsonObject { ["name"] = o.Username, ["isPersonal"] = true, ["isReadOnly"] = false, ["accountCapabilities"] = accountCaps } },
            ["primaryAccounts"] = new JsonObject
            {
                ["urn:ietf:params:jmap:mail"] = o.AccountId,
                ["urn:ietf:params:jmap:submission"] = o.AccountId,
                ["urn:ietf:params:jmap:quota"] = o.AccountId,
            },
            ["username"] = o.Username,
            ["apiUrl"] = Url("/jmap/"),
            ["downloadUrl"] = Url("/jmap/download/{accountId}/{blobId}/{name}?accept={type}"),
            ["uploadUrl"] = Url("/jmap/upload/{accountId}/"),
            ["state"] = "S" + Volatile.Read(ref _sessionState).ToString(CultureInfo.InvariantCulture),
        };
        AddSharedAccounts(session, o, accountCaps);
        if (o.Push)
        {
            session["eventSourceUrl"] = Url("/jmap/eventsource/?types={types}&closeafter={closeafter}&ping={ping}");
        }

        return session;
    }

    private async Task<HttpResponseMessage> ApiAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var body = Parse(bytes);
        Log("POST api " + string.Join(",", (body?["methodCalls"] as JsonArray ?? []).Select(c => (string?)c?[0])));
        var inFlight = Interlocked.Increment(ref _inFlight);
        try
        {
            lock (_lock)
            {
                MaxInFlight = Math.Max(MaxInFlight, inFlight);
            }

            if (Volatile.Read(ref _hold) is { } hold)
            {
                await hold.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            if (TakeHttpFault() is { } fault)
            {
                return fault;
            }

            bool failThis;
            lock (_lock)
            {
                failThis = ++_apiRequests == _failApiAt;
            }

            if (failThis)
            {
                var (failStatus, failJson) = FakeApi.Problem(500, "urn:ietf:params:jmap:error:serverFail", "Injected failure.", null);
                return Json((HttpStatusCode)failStatus, failJson, "application/problem+json");
            }

            var (status, json) = bytes.Length > Options.MaxSizeRequest
                ? FakeApi.Problem(400, "urn:ietf:params:jmap:error:limit", "Request too large.", "maxSizeRequest")
                : body is null ? FakeApi.Problem(400, "urn:ietf:params:jmap:error:notJSON", "Not JSON.", null) : Api.Run(body);
            return Json((HttpStatusCode)status, json, status == 200 ? "application/json" : "application/problem+json");
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private HttpResponseMessage? TakeHttpFault()
    {
        FakeFault fault;
        lock (_lock)
        {
            if (_httpFaults.Count == 0)
            {
                return null;
            }

            fault = _httpFaults.Dequeue();
        }

        switch (fault)
        {
            case FakeFault.TooManyRequests or FakeFault.Unavailable:
                var r = new HttpResponseMessage(fault == FakeFault.TooManyRequests ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(RetryAfterSeconds));
                return r;
            default:
                var (status, json) = FakeApi.Problem(500, "urn:ietf:params:jmap:error:serverFail", "Injected failure.", null);
                return Json((HttpStatusCode)status, json, "application/problem+json");
        }
    }

    /// <summary>The Retry-After the 429/503 faults send, in seconds.</summary>
    public int RetryAfterSeconds { get; set; } = 1;

    private HttpResponseMessage Download(string path)
    {
        Log("GET download");
        var parts = path.Split('/');
        var blob = parts.Length > 4 ? Uri.UnescapeDataString(parts[4]) : "";
        return Store.Blob(blob) is { } bytes
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> UploadAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Log("POST upload");
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length > Options.MaxSizeUpload)
        {
            return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
        }

        var id = Store.PutBlob(bytes);
        var type = request.Content?.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return Json(HttpStatusCode.Created, new JsonObject { ["accountId"] = Options.AccountId, ["blobId"] = id, ["type"] = type, ["size"] = bytes.Length });
    }

    private HttpResponseMessage EventSource(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);
        Log($"GET eventsource types={query.GetValueOrDefault("types")} closeafter={query.GetValueOrDefault("closeafter")} ping={query.GetValueOrDefault("ping")}");
        if (!int.TryParse(query.GetValueOrDefault("ping"), CultureInfo.InvariantCulture, out var ping) || (ping != 0 && ping < 30))
        {
            // Stalwart refuses a ping interval under 30 s
            return Json(HttpStatusCode.BadRequest, FakeApi.Problem(400, "about:blank", "ping must be at least 30", null).Body, "application/problem+json");
        }

        var stream = Push.Open((query.GetValueOrDefault("types") ?? "*").Split(','));
        var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private async Task<HttpResponseMessage> OAuthAsync(HttpRequestMessage request, string path, CancellationToken ct)
    {
        Log($"{request.Method} {path}");
        if (path.StartsWith("/.well-known/oauth", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["issuer"] = Options.Origin,
                ["authorization_endpoint"] = Options.Origin + "/oauth/authorize",
                ["token_endpoint"] = Options.Origin + "/oauth/token",
                ["device_authorization_endpoint"] = Options.Origin + "/oauth/device",
                ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:device_code"),
                ["code_challenge_methods_supported"] = new JsonArray("S256"),
            });
        }

        var form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!path.Equals("/oauth/token", StringComparison.Ordinal) || !form.Contains("grant_type=refresh_token", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = "unsupported_grant_type" });
        }

        var token = IssueToken();

        return Json(HttpStatusCode.OK, new JsonObject { ["access_token"] = token, ["token_type"] = "Bearer", ["expires_in"] = 3600, ["refresh_token"] = "fake-refresh-" + token });
    }

    private static JsonObject? Parse(byte[] bytes)
    {
        try
        {
            return JsonNode.Parse(bytes) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The M2 capabilities, each off by its own flag so the M1 session stays as it was.</summary>
    private static void AddM2Capabilities(JsonObject caps, FakeJmapOptions o)
    {
        if (o.SieveCapability)
        {
            caps["urn:ietf:params:jmap:sieve"] = new JsonObject();
        }

        if (o.FileNodeCapability)
        {
            caps["urn:ietf:params:jmap:filenode"] = new JsonObject();
        }

        if (o.CalendarsCapability)
        {
            caps["urn:ietf:params:jmap:calendars"] = new JsonObject();
        }
    }

    // M2 unified: the group accounts the login reaches (Stalwart lists them with isPersonal false)
    private static void AddSharedAccounts(JsonObject session, FakeJmapOptions o, JsonObject accountCaps)
    {
        foreach (var shared in o.SharedAccounts)
        {
            session["accounts"]![shared] = new JsonObject { ["name"] = shared, ["isPersonal"] = false, ["isReadOnly"] = false, ["accountCapabilities"] = accountCaps.DeepClone() };
        }
    }
}
