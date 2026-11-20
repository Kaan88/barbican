// Mirrors: Rust jmap.rs:228 post (maxSizeRequest checked up front, the maxConcurrentRequests gate, 401 renewal, one retry of a
// read-only batch after 429/503 with Retry-After capped at 10 s, request-level errors as problem details, the sessionState check),
// :272 request (split by maxCallsInRequest), :216 refresh_session, :772 download_from, :797 upload_to (size check, upload gate),
// :1108 watch (the event source request); Bulwark lib/jmap/client.ts:2384 request, :2463 networkRequest, :6876 getBlobDownloadUrl;
// RFC 8620 §3.3-§3.6 (request, response, errors), §3.4 (sessionState), §6.1-§6.2 (blobs), §7.3 (EventSource); PORT-PLAN.md §3.4,
// §4.6 (read-only mode); Stalwart crates/jmap/src/api/request.rs. Owner jmap.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Transport;

/// <summary>The JMAP client of one login.</summary>
public sealed class JmapClient : IJmapClient, IDisposable
{
    private readonly IJmapHttp _http;
    private readonly AuthLatch _auth;
    private readonly JmapClientOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _requests;
    private readonly SemaphoreSlim _uploads;
    private JmapSession _session;

    public JmapClient(IJmapHttp http, IAccessTokenSource auth, JmapSession session, JmapClientOptions options, TimeProvider time, JmapTrace trace)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(trace);
        _http = http;
        _auth = AuthLatch.Over(auth);
        _session = session;
        _options = options;
        _time = time;
        Trace = trace;
        _requests = new SemaphoreSlim(session.Limits.MaxConcurrentRequests);
        _uploads = new SemaphoreSlim(session.Limits.MaxConcurrentUpload);
    }

    /// <inheritdoc/>
    public JmapSession Session => Volatile.Read(ref _session);

    /// <summary>The latch over this client's token source: closed once the server refused the credentials for good (R2-01).</summary>
    public AuthLatch Auth => _auth;

    /// <inheritdoc/>
    public bool IsReadOnly => _options.ReadOnly;

    /// <inheritdoc/>
    public JmapTrace Trace { get; }

    /// <summary>API requests in flight (for the gate tests).</summary>
    public int RequestsInFlight => Session.Limits.MaxConcurrentRequests - _requests.CurrentCount;

    /// <inheritdoc/>
    public async Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsReadOnly && ReadOnlyGuard.FirstWrite(request.MethodCalls) is { } write)
        {
            return Result<JmapResponse>.Fail(new ReadOnlyViolation(write));
        }

        var session = Session;
        var use = request.Using.Count > 0 ? request.Using : session.CapabilityNames;
        var responses = new List<JsonArray>();
        var sessionState = "";
        foreach (var batch in RequestSplitter.SplitCalls(request.MethodCalls, session.Limits.MaxCallsInRequest))
        {
            var r = await PostAsync(use, [.. batch.Cast<JsonArray>()], request.BeforeSend, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<JmapResponse>.Fail(r.Error!);
            }

            responses.AddRange(r.Value!.MethodResponses);
            sessionState = r.Value.SessionState;
        }

        return Result<JmapResponse>.Ok(new JmapResponse { MethodResponses = responses, SessionState = sessionState });
    }

    /// <inheritdoc/>
    public async Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct)
    {
        var template = Session.DownloadUrl;
        if (template.Length == 0)
        {
            return Result<Stream>.Fail(new TransportError("The server has no download URL.", Retryable: false));
        }

        var url = new Uri(UriTemplates.Download(template, accountId, blobId, name, type));
        Trace.Event("DOWNLOAD", "");
        var sent = await TimedAsync(
            c => AuthorizedHttp.SendAsync(_http.ClientFor(url), _auth, url, Allowed, () => new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, c),
            ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<Stream>.Fail(sent.Error!);
        }

        var response = sent.Value!;
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            return Result<Stream>.Fail(status == 401 ? Expired() : new TransportError($"Download failed ({status})", Retryable: status >= 500));
        }

        // no overall deadline: a big attachment on a slow link must not be cut off (Rust jmap.rs:779); the caller owns the stream
        return Result<Stream>.Ok(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Uploads a blob (RFC 8620 §6.1): checked against maxSizeUpload first, through the maxConcurrentUpload gate.</summary>
    public async Task<Result<UploadedBlob>> UploadAsync(string accountId, ReadOnlyMemory<byte> data, string type, CancellationToken ct)
    {
        if (IsReadOnly)
        {
            return Result<UploadedBlob>.Fail(new ReadOnlyViolation("Blob/upload"));
        }

        var session = Session;
        if (data.Length > session.Limits.MaxSizeUpload)
        {
            return Result<UploadedBlob>.Fail(TooLargeUpload(session));
        }

        if (session.UploadUrl.Length == 0)
        {
            return Result<UploadedBlob>.Fail(new TransportError("The server has no upload URL.", Retryable: false));
        }

        var url = new Uri(UriTemplates.Upload(session.UploadUrl, accountId));
        var mime = string.IsNullOrEmpty(type) ? "application/octet-stream" : type;
        await _uploads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            HttpRequestMessage Make() => new(HttpMethod.Post, url)
            {
                Content = new ReadOnlyMemoryContent(data) { Headers = { ContentType = MediaTypeHeaderValue.Parse(mime) } },
            };
            var sent = await AuthorizedHttp.SendAsync(_http.ClientFor(url), _auth, url, Allowed, Make, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!sent.IsOk)
            {
                return Result<UploadedBlob>.Fail(sent.Error!);
            }

            using var response = sent.Value!;
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                return Result<UploadedBlob>.Fail(status switch
                {
                    413 => TooLargeUpload(session),
                    401 => Expired(),
                    _ => ProblemMessages.ToError(status, ParseObject(text) ?? []),
                });
            }

            var v = ParseObject(text);
            return v.Str("blobId") is { } blob
                ? Result<UploadedBlob>.Ok(new UploadedBlob(blob, v.Number("size") is var s and > 0 ? (long)s : data.Length, v.Str("type") ?? mime))
                : Result<UploadedBlob>.Fail(new TransportError("No blobId in upload response", Retryable: false));
        }
        finally
        {
            _uploads.Release();
        }
    }

    /// <inheritdoc/>
    public T? AccountCapability<T>(string accountId, string capability)
        where T : class
    {
        if (Session.CapabilityObject(accountId, capability) is not { } element)
        {
            return null;
        }

        if (typeof(T) == typeof(JsonObject))
        {
            return JsonNode.Parse(element.GetRawText()) as T;
        }

        try
        {
            return JmapJsonContext.Default.GetTypeInfo(typeof(T)) is { } info ? element.Deserialize(info) as T : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Stream>> OpenEventSourceAsync(IReadOnlyList<string> types, int pingSeconds, CancellationToken ct)
    {
        var template = Session.EventSourceUrl;
        if (template.Length == 0)
        {
            return Result<Stream>.Fail(new TransportError("No push support.", Retryable: false));
        }

        var url = new Uri(UriTemplates.EventSource(template, types, pingSeconds));
        HttpRequestMessage Make()
        {
            var m = new HttpRequestMessage(HttpMethod.Get, url);
            m.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            m.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            return m;
        }

        var sent = await TimedAsync(c => AuthorizedHttp.SendAsync(_http.ClientFor(url), _auth, url, Allowed, Make, HttpCompletionOption.ResponseHeadersRead, c), ct)
            .ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<Stream>.Fail(sent.Error!);
        }

        var response = sent.Value!;
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            return Result<Stream>.Fail(status == 401 ? Expired() : new TransportError($"Push failed ({status})", Retryable: true));
        }

        return Result<Stream>.Ok(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Re-reads the session resource (RFC 8620 §3.4); the old session stays when that fails.</summary>
    public async Task<bool> RefreshSessionAsync(CancellationToken ct)
    {
        // the server the user typed stays the one credentials go to; a session URL off its origin is reached by discovery again
        var session = Session;
        var server = new ServerEndpoint(new Uri(session.ServerUrl));
        var url = new Uri(session.SessionUrl);
        if (!string.Equals(url.GetLeftPart(UriPartial.Authority), server.BaseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            url = server.DiscoveryUri;
        }

        var fetched = await SessionFetcher.FetchAsync(_http, _auth, server, url, _options.RequestTimeout, _time, ct).ConfigureAwait(false);
        Trace.Event("SESSION", "refresh");
        if (!fetched.IsOk)
        {
            return false;
        }

        Volatile.Write(ref _session, fetched.Value!);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _requests.Dispose();
        _uploads.Dispose();
    }

    // F-02: credentials only to the origins of this login's session
    private bool Allowed(Uri url) => Session.AllowsCredentials(url);

    private static AuthError Expired() => new("Session expired; sign in again.", NeedsSignIn: true);

    private static TransportError TooLargeUpload(JmapSession s) =>
        new($"The file is larger than the server allows ({s.Limits.MaxSizeUpload / 1_000_000} MB).", Retryable: false);

    private static JsonObject? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Result<JmapResponse>> PostAsync(IReadOnlyList<string> use, IReadOnlyList<JsonArray> calls,
        Func<Func<bool>, CancellationToken, Task<bool>>? beforeSend, CancellationToken ct)
    {
        var session = Session;
        var body = RequestBody(use, calls);
        if (body.Length > session.Limits.MaxSizeRequest)
        {
            return Result<JmapResponse>.Fail(new TransportError(
                $"The request is larger than the server accepts ({session.Limits.MaxSizeRequest / 1000} KB).", Retryable: false));
        }

        var api = new Uri(session.ApiUrl);
        var readOnly = calls.All(c => c.Count > 0 && c[0].AsString() is { } m && ReadOnlyGuard.IsReadMethod(m));
        for (var attempt = 0; ; attempt++)
        {
            var sent = await TimedAsync(c => PostOnceAsync(api, body, beforeSend, c), ct).ConfigureAwait(false);
            if (!sent.IsOk)
            {
                Trace.Request(calls, null);
                return Result<JmapResponse>.Fail(sent.Error!);
            }

            var (status, retry, text) = sent.Value;
            if (RetryAfter.IsRetryStatus(status) && readOnly && attempt == 0)
            {
                Trace.Event("RETRY", ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
                await Task.Delay(RetryAfter.Delay(retry, _time.GetUtcNow(), _options.MaxRetryAfter), _time, ct).ConfigureAwait(false);
                continue;
            }

            return await ReadResponseAsync(calls, status, text, ct).ConfigureAwait(false);
        }
    }

    private async Task<Result<(HttpStatusCode Status, RetryConditionHeaderValue? Retry, string Text)>> PostOnceAsync(
        Uri api, byte[] body, Func<Func<bool>, CancellationToken, Task<bool>>? beforeSend, CancellationToken ct)
    {
        await _requests.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            HttpRequestMessage Make()
            {
                var m = new HttpRequestMessage(HttpMethod.Post, api) { Content = new ByteArrayContent(body) };
                m.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                m.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return m;
            }

            var sent = await AuthorizedHttp.SendAsync(_http.ClientFor(api), _auth, api, Allowed, Make, HttpCompletionOption.ResponseContentRead, ct, beforeSend).ConfigureAwait(false);
            if (!sent.IsOk)
            {
                return Result<(HttpStatusCode, RetryConditionHeaderValue?, string)>.Fail(sent.Error!);
            }

            using var response = sent.Value!;
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Result<(HttpStatusCode, RetryConditionHeaderValue?, string)>.Ok((response.StatusCode, response.Headers.RetryAfter, text));
        }
        finally
        {
            _requests.Release();
        }
    }

    private async Task<Result<JmapResponse>> ReadResponseAsync(IReadOnlyList<JsonArray> calls, HttpStatusCode status, string text, CancellationToken ct)
    {
        var code = (int)status;
        if (status == HttpStatusCode.Unauthorized)
        {
            Trace.Request(calls, null);
            return Result<JmapResponse>.Fail(Expired());
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            Trace.Request(calls, null);
            return Result<JmapResponse>.Fail(new TransportError("The server is rate limiting requests; try again shortly.", Retryable: true));
        }

        if (code >= 400)
        {
            Trace.Request(calls, null);
            return Result<JmapResponse>.Fail(ProblemMessages.ToError(code, ParseObject(text) ?? []));
        }

        var v = ParseObject(text);
        if (v.Arr("methodResponses") is not { } list)
        {
            Trace.Request(calls, null);
            return Result<JmapResponse>.Fail(new TransportError("Bad JMAP response", Retryable: false));
        }

        var responses = list.OfType<JsonArray>().ToList();
        foreach (var r in responses)
        {
            list.Remove(r);
        }

        Trace.Request(calls, responses);
        var sessionState = v.Str("sessionState") ?? "";
        if (sessionState.Length > 0 && Session.State.Length > 0 && !string.Equals(sessionState, Session.State, StringComparison.Ordinal))
        {
            await RefreshSessionAsync(ct).ConfigureAwait(false);
        }

        return Result<JmapResponse>.Ok(new JmapResponse { MethodResponses = responses, SessionState = sessionState });
    }

    private async Task<Result<T>> TimedAsync<T>(Func<CancellationToken, Task<Result<T>>> run, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(_options.RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            return await run(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<T>.Fail(new TransportError("The server did not answer in time.", Retryable: true));
        }
        catch (HttpRequestException e)
        {
            return Result<T>.Fail(new TransportError($"Network error: {e.Message}", Retryable: true));
        }
    }

    private static byte[] RequestBody(IReadOnlyList<string> use, IReadOnlyList<JsonArray> calls)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartArray("using");
            foreach (var u in use)
            {
                w.WriteStringValue(u);
            }

            w.WriteEndArray();
            w.WriteStartArray("methodCalls");
            foreach (var c in calls)
            {
                c.WriteTo(w);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
