// Mirrors: Rust oauth.rs:38 http (15 s timeout, redirects never followed); spec B-OAUTH-006. Requests go through jmap's shared clients
// (IJmapHttp: one HttpClient per origin, no automatic redirects), so tests and the automation build reach FakeJmap the same way.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Auth;

/// <summary>The few HTTP calls of sign-in: GET a JSON document, POST a form, POST JSON.</summary>
public sealed class OAuthHttp(IJmapHttp http, TimeProvider time)
{
    /// <summary>The deadline of every OAuth request (Rust oauth.rs:40).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>GET <paramref name="url"/> as JSON.</summary>
    public Task<OAuthResponse> GetJsonAsync(Uri url, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);

    /// <summary>POST an <c>application/x-www-form-urlencoded</c> body.</summary>
    public Task<OAuthResponse> PostFormAsync(Uri url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) }, ct);

    /// <summary>POST a JSON body.</summary>
    public Task<OAuthResponse> PostJsonAsync(Uri url, JsonObject body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var text = body.ToJsonString();
        return SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(text, Encoding.UTF8, "application/json") }, ct);
    }

    private async Task<OAuthResponse> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var request = make();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await http.ClientFor(request.RequestUri!).SendAsync(request, linked.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return new OAuthResponse(response.StatusCode, Reason(response), Parse(text), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new OAuthResponse(default, "timeout", null, "The server did not answer in time.");
        }
        catch (HttpRequestException e)
        {
            return new OAuthResponse(default, "network", null, $"error sending request: {e.Message}");
        }
    }

    private static string Reason(HttpResponseMessage response) =>
        response.ReasonPhrase is { Length: > 0 } r ? r : Enum.IsDefined(response.StatusCode) ? Words(response.StatusCode) : "";

    // "BadRequest" -> "Bad Request", as reqwest prints a status (Rust error strings start "Token request failed (400 Bad Request)")
    private static string Words(HttpStatusCode code)
    {
        var name = code.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append(' ');
            }

            sb.Append(name[i]);
        }

        return sb.ToString();
    }

    private static JsonObject? Parse(string text)
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
}
