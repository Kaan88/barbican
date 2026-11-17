// An in-process JMAP server as an HttpMessageHandler (docs/milestones/M1-plan.md §3.2, §6.1): the session resource (RFC 8620 §2,
// /.well-known/jmap §2.2), the API (§3), blobs (§6), the event source (§7.3), OAuth endpoints (RFC 8414 metadata, RFC 6749 §6
// refresh), with Stalwart state strings and fault injection (401, 429/503 with Retry-After, RFC 7807 problems). Synthetic data,
// example.org only. Owner jmap.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>The fake JMAP server.</summary>
public sealed partial class FakeJmapServer : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly Queue<FakeFault> _httpFaults = new();
    private readonly HashSet<string> _tokens = new(StringComparer.Ordinal) { "fake-access-1" };
    private readonly List<string> _requests = [];
    private TaskCompletionSource? _hold;
    private int _sessionState = 1;
    private int _inFlight;
    private int _tokenSerial = 1;

    public FakeJmapServer(FakeJmapOptions? options = null, bool seed = true)
    {
        Options = options ?? new FakeJmapOptions();
        Store = new FakeStore();
        Api = new FakeApi(Store, Options, () => "S" + Volatile.Read(ref _sessionState).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Push = new PushHub(Store, Options.AccountId);
        if (seed)
        {
            FakeSeed.Fill(Store);
        }
    }

    public FakeJmapOptions Options { get; }

    public FakeStore Store { get; }

    public FakeApi Api { get; }

    public PushHub Push { get; }

    /// <summary>The URL the client starts from.</summary>
    public string Base => Options.Origin;

    /// <summary>A valid bearer token.</summary>
    public const string AccessToken = "fake-access-1";

    /// <summary>The highest number of API requests seen at once.</summary>
    public int MaxInFlight { get; private set; }

    /// <summary>OAuth refresh grants served.</summary>
    public int Refreshes { get; private set; }

    /// <summary>Every request as "METHOD path" (API requests as "POST api Method/a,Method/b").</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The API requests only, each as its method names.</summary>
    public IReadOnlyList<string> ApiCalls => [.. Requests.Where(r => r.StartsWith("POST api ", StringComparison.Ordinal)).Select(r => r["POST api ".Length..])];

    /// <summary>Queues an HTTP-level fault for the next matching request.</summary>
    public void Inject(FakeFault fault)
    {
        if (fault is FakeFault.CannotCalculateChanges or FakeFault.TooManyChanges or FakeFault.QueryStateWentBack or FakeFault.EmailChangesFail)
        {
            Api.Inject(fault);
            return;
        }

        lock (_lock)
        {
            _httpFaults.Enqueue(fault);
        }
    }

    /// <summary>Changes the session state; the next API response carries it (RFC 8620 §3.4 sessionState).</summary>
    public void BumpSessionState() => Interlocked.Increment(ref _sessionState);

    /// <summary>Stops accepting a bearer token (it expired).</summary>
    public void ExpireToken(string token)
    {
        lock (_lock)
        {
            _tokens.Remove(token);
        }
    }

    /// <summary>Hands out a new access token, as a refresh grant does.</summary>
    public string IssueToken()
    {
        lock (_lock)
        {
            Refreshes++;
            var token = "fake-access-" + (++_tokenSerial).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _tokens.Add(token);
            return token;
        }
    }

    /// <summary>Holds every API response until the returned object is disposed (for the concurrency gate tests).</summary>
    public IDisposable HoldApi()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _hold = tcs;
        }

        return new Release(() =>
        {
            lock (_lock)
            {
                _hold = null;
            }

            tcs.TrySetResult();
        });
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri!.AbsolutePath;
        var origin = request.RequestUri.GetLeftPart(UriPartial.Authority);
        if (!string.Equals(origin, Options.Origin, StringComparison.OrdinalIgnoreCase) && !string.Equals(origin, Options.RedirectOrigin, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException($"No such host: {request.RequestUri.Host}");
        }

        if (path.StartsWith("/oauth/", StringComparison.Ordinal) || path.StartsWith("/.well-known/oauth", StringComparison.Ordinal))
        {
            return await OAuthAsync(request, path, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(path, "/.well-known/jmap", StringComparison.Ordinal) && Options.RedirectWellKnown)
        {
            Log($"GET {path}");
            var r = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            r.Headers.Location = Options.RedirectOrigin is { } other ? new Uri(other + "/jmap/session") : new Uri("/jmap/session", UriKind.Relative);
            return r;
        }

        if (Authorized(request) is { } denied)
        {
            Log($"{request.Method} {path} 401");
            return denied;
        }

        return path switch
        {
            "/.well-known/jmap" or "/jmap/session" => Json(HttpStatusCode.OK, Session(LogReturn(request, path))),
            "/jmap/" or "/jmap" => await ApiAsync(request, cancellationToken).ConfigureAwait(false),
            _ when path.StartsWith("/jmap/download/", StringComparison.Ordinal) => Download(path),
            _ when path.StartsWith("/jmap/upload/", StringComparison.Ordinal) => await UploadAsync(request, cancellationToken).ConfigureAwait(false),
            _ when path.StartsWith("/jmap/eventsource", StringComparison.Ordinal) => EventSource(request),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, mediaType) };

    private string LogReturn(HttpRequestMessage request, string path)
    {
        Log($"{request.Method} {path}");
        return path;
    }

    private void Log(string line)
    {
        lock (_lock)
        {
            _requests.Add(line);
        }
    }

    private HttpResponseMessage? Authorized(HttpRequestMessage request)
    {
        lock (_lock)
        {
            if (_httpFaults.Count > 0 && _httpFaults.Peek() == FakeFault.Unauthorized)
            {
                _httpFaults.Dequeue();
                return Unauthorized();
            }
        }

        var auth = request.Headers.Authorization;
        string Basic(string password) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Options.Username}:{password}"));
        if (Options.Otp is { } otp && auth is { Scheme: "Basic" } && string.Equals(auth.Parameter, Basic(Options.Password), StringComparison.Ordinal))
        {
            // Stalwart: the password is right, the second factor is missing (Bulwark lib/jmap/client.ts:2165)
            var (_, problem) = FakeApi.Problem(402, "about:blank", "Enter your TOTP code", null);
            problem["title"] = "TOTP code required";
            return Json(HttpStatusCode.PaymentRequired, problem, "application/problem+json");
        }

        var expected = Options.Otp is { } code ? Basic($"{Options.Password}${code}") : Basic(Options.Password);
        var ok = auth?.Scheme switch
        {
            "Basic" => string.Equals(auth.Parameter, expected, StringComparison.Ordinal),
            "Bearer" => Valid(auth.Parameter),
            _ => false,
        };
        return ok ? null : Unauthorized();
    }

    private bool Valid(string? token)
    {
        lock (_lock)
        {
            return token is not null && _tokens.Contains(token);
        }
    }

    private static HttpResponseMessage Unauthorized()
    {
        var r = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        r.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\""));
        return r;
    }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
