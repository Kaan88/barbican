// The OAuth half of a Stalwart server in front of FakeJmap (which serves only the metadata and the refresh grant): the authorization endpoint
// (redirects to redirect_uri with code and state, RFC 6749 §4.1.2), dynamic registration (RFC 7591), and the token endpoint's
// authorization_code grant with the PKCE check (RFC 7636 §4.6: BASE64URL(SHA256(code_verifier)) must equal the stored challenge) and the
// refresh grant with rotation (a used refresh token is invalid_grant afterwards, as Stalwart crates/common/src/auth/oauth/token.rs).
// Access tokens come from FakeJmapServer.IssueToken, so the fake JMAP API accepts them. Everything else goes to the inner server.
// Request to jmap (FABLE-REVIEW "M1 stream: accounts"): fold this into tests/Barbican.FakeJmap so the AUTOMATION build can use it.

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Auth;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Auth;

internal sealed class FakeOAuthProvider : DelegatingHandler
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string Challenge, string Redirect, string Client)> _codes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refresh = new(StringComparer.Ordinal);
    private int _serial;

    public FakeOAuthProvider(FakeJmapServer server, bool registration = true)
        : base(server)
    {
        Server = server;
        Registration = registration;
    }

    public FakeJmapServer Server { get; }

    public bool Registration { get; }

    public string Origin => Server.Options.Origin;

    /// <summary>Clients registered.</summary>
    public int Registrations { get; private set; }

    /// <summary>Code redemptions whose verifier did not match.</summary>
    public int PkceFailures { get; private set; }

    /// <summary>Refresh grants answered.</summary>
    public int RefreshGrants { get; private set; }

    /// <summary>Answer the next authorization request with this error instead of a code.</summary>
    public string? NextError { get; set; }

    /// <summary>The token endpoint answers 503 while set.</summary>
    public bool TokenEndpointDown { get; set; }

    /// <summary>Adds a refresh token the token endpoint accepts.</summary>
    public string AddRefreshToken()
    {
        lock (_lock)
        {
            var t = "fake-refresh-" + (++_serial).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _refresh.Add(t);
            return t;
        }
    }

    public bool IsValidRefresh(string token)
    {
        lock (_lock)
        {
            return _refresh.Contains(token);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        if (!string.Equals(uri.GetLeftPart(UriPartial.Authority), Origin, StringComparison.OrdinalIgnoreCase))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return path switch
        {
            "/.well-known/oauth-authorization-server" => Json(HttpStatusCode.OK, Metadata()),
            "/.well-known/openid-configuration" => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ when path.StartsWith("/api/discover/", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/oauth/authorize" => Authorize(uri),
            "/oauth/register" => await RegisterAsync(request, cancellationToken).ConfigureAwait(false),
            "/oauth/token" => await TokenAsync(request, cancellationToken).ConfigureAwait(false),
            _ => await base.SendAsync(request, cancellationToken).ConfigureAwait(false),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(string error) => Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = error });

    private JsonObject Metadata()
    {
        var m = new JsonObject
        {
            ["issuer"] = Origin,
            ["authorization_endpoint"] = Origin + "/oauth/authorize",
            ["token_endpoint"] = Origin + "/oauth/token",
            ["scopes_supported"] = new JsonArray("openid", "offline_access", "urn:ietf:params:oauth:scope:mail"),
            ["code_challenge_methods_supported"] = new JsonArray("S256"),
        };
        if (Registration)
        {
            m["registration_endpoint"] = Origin + "/oauth/register";
        }

        return m;
    }

    private HttpResponseMessage Authorize(Uri uri)
    {
        var q = LoopbackListener.ParseQuery(uri.Query);
        if (!q.TryGetValue("redirect_uri", out var redirect) || !q.TryGetValue("state", out var state) || !q.TryGetValue("code_challenge", out var challenge)
            || !string.Equals(q.GetValueOrDefault("code_challenge_method"), "S256", StringComparison.Ordinal) || !q.TryGetValue("client_id", out var client))
        {
            return Error("invalid_request");
        }

        string target;
        lock (_lock)
        {
            if (NextError is { } error)
            {
                NextError = null;
                target = $"{redirect}?error={Uri.EscapeDataString(error)}&state={Uri.EscapeDataString(state)}";
            }
            else
            {
                var code = "fake-code-" + (++_serial).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _codes[code] = (challenge, redirect, client);
                target = $"{redirect}?code={code}&state={Uri.EscapeDataString(state)}";
            }
        }

        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(target);
        return r;
    }

    private async Task<HttpResponseMessage> RegisterAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct).ConfigureAwait(false)) as JsonObject;
        if (body?["token_endpoint_auth_method"]?.GetValue<string>() is not "none" || body["redirect_uris"] is not JsonArray)
        {
            return Error("invalid_client_metadata");
        }

        lock (_lock)
        {
            Registrations++;
        }

        return Json(HttpStatusCode.Created, new JsonObject { ["client_id"] = "registered-client-" + Registrations.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (TokenEndpointDown)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var form = LoopbackListener.ParseQuery(await request.Content!.ReadAsStringAsync(ct).ConfigureAwait(false));
        switch (form.GetValueOrDefault("grant_type"))
        {
            case "authorization_code":
                lock (_lock)
                {
                    if (!_codes.Remove(form.GetValueOrDefault("code") ?? "", out var issued)
                        || !string.Equals(issued.Redirect, form.GetValueOrDefault("redirect_uri"), StringComparison.Ordinal)
                        || !string.Equals(issued.Client, form.GetValueOrDefault("client_id"), StringComparison.Ordinal))
                    {
                        return Error("invalid_grant");
                    }

                    if (!string.Equals(Pkce.Challenge(form.GetValueOrDefault("code_verifier") ?? ""), issued.Challenge, StringComparison.Ordinal))
                    {
                        PkceFailures++;
                        return Error("invalid_grant");
                    }
                }

                return Tokens(rotate: null);
            case "refresh_token":
                var used = form.GetValueOrDefault("refresh_token") ?? "";
                lock (_lock)
                {
                    RefreshGrants++;
                    if (!_refresh.Remove(used))
                    {
                        return Error("invalid_grant");
                    }
                }

                return Tokens(rotate: used);
            default:
                return Error("unsupported_grant_type");
        }
    }

    private HttpResponseMessage Tokens(string? rotate)
    {
        var access = Server.IssueToken();
        var refresh = AddRefreshToken();
        _ = rotate;
        return Json(HttpStatusCode.OK, new JsonObject { ["access_token"] = access, ["token_type"] = "Bearer", ["expires_in"] = 3600, ["refresh_token"] = refresh });
    }
}
