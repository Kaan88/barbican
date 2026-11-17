// Mirrors: Rust oauth.rs:164 sign_in (discover, loopback listener, client id, PKCE and state, the authorization URL with scope and
// login_hint, the browser, the callback, the code redeemed at the token endpoint); RFC 6749 §4.1 (authorization code grant), RFC 7636
// (PKCE S256), RFC 8252 (native apps); Stalwart crates/http/src/auth/oauth/auth.rs (authorize), token.rs (authorization_code grant checks
// the code_verifier). Bulwark lib/oauth/token-exchange.ts posts the same form. spec B-OAUTH-001, B-OAUTH-009, B-OAUTH-011, B-OAUTH-012.
// Fable review 1 F-04: the callback is checked for state and RFC 9207 iss (CallbackCheck); discovery refuses non-https external endpoints.
// M1 hotfix (sign-in): the query is form-urlencoded exactly as Rust's Url::parse_with_params writes it (space as '+', only A-Z a-z 0-9 * - . _
// left as they are; WHATWG URL application/x-www-form-urlencoded serializer), so the authorization request the server's page receives is the
// one the Rust app sends, byte for byte apart from the random values.

using Barbican.Core.Common;

namespace Barbican.Core.Auth;

/// <summary>The interactive OAuth sign-in: authorization code with PKCE and a loopback redirect.</summary>
public sealed class OAuthFlow(OAuthDiscovery discovery, OAuthClientRegistry clients, OAuthHttp http, IAuthorizationLauncher launcher, OAuthFlowOptions options, TimeProvider time)
{
    /// <summary>
    /// Signs in at <paramref name="baseUri"/>; <paramref name="account"/> (may be empty) picks Stalwart's per-account discovery and is sent as
    /// <c>login_hint</c>; <paramref name="fallbackClient"/> is used when the server offers no dynamic registration.
    /// </summary>
    public async Task<Result<OAuthTokens>> SignInAsync(Uri baseUri, string account, string fallbackClient, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(fallbackClient);
        var meta = await discovery.DiscoverAsync(baseUri, account ?? "", ct).ConfigureAwait(false);
        if (!meta.IsOk)
        {
            return Result<OAuthTokens>.Fail(meta.Error!);
        }

        var started = LoopbackListener.Start(options.LoopbackPort);
        if (!started.IsOk)
        {
            return Result<OAuthTokens>.Fail(started.Error!);
        }

        using var listener = started.Value!;
        var redirect = listener.RedirectUri;
        var client = await clients.ClientIdAsync(meta.Value!, redirect, string.IsNullOrWhiteSpace(fallbackClient) ? OAuthClientRegistry.DefaultClientId : fallbackClient.Trim(), ct).ConfigureAwait(false);
        var verifier = Pkce.NewVerifier();
        var state = Pkce.NewState();
        var url = AuthorizeUri(meta.Value!, client, redirect, Pkce.Challenge(verifier), state, account);

        // listen before the browser opens: a fast provider (or the automation launcher, which delivers the redirect itself) answers at once
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var wait = listener.WaitForCodeAsync(new CallbackCheck(state, meta.Value!.Issuer, meta.Value!.IssParameterSupported), options.Timeout, time, stop.Token);
        var opened = await launcher.OpenAsync(url, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await SwallowAsync(wait).ConfigureAwait(false);
            return Result<OAuthTokens>.Fail(opened.Error!);
        }

        var code = await wait.ConfigureAwait(false);
        if (!code.IsOk)
        {
            return Result<OAuthTokens>.Fail(code.Error!);
        }

        var r = await http.PostFormAsync(meta.Value!.Token, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code.Value!,
            ["redirect_uri"] = redirect.AbsoluteUri,
            ["client_id"] = client,
            ["code_verifier"] = verifier,
        }, ct).ConfigureAwait(false);
        return TokenResponse.Parse(r, meta.Value!.Token, client, null, time.GetUtcNow());
    }

    /// <summary>The authorization URL (the endpoint's own query is kept; Rust Url::parse_with_params).</summary>
    public static Uri AuthorizeUri(OAuthMetadata meta, string client, Uri redirect, string challenge, string state, string? account)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(redirect);
        var q = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", client),
            new("redirect_uri", redirect.AbsoluteUri),
            new("code_challenge", challenge),
            new("code_challenge_method", "S256"),
            new("state", state),
        };
        if (meta.Scope.Length > 0)
        {
            q.Add(new("scope", meta.Scope));
        }

        if (account?.Trim() is { Length: > 0 } hint)
        {
            q.Add(new("login_hint", hint));
        }

        var b = new UriBuilder(meta.Authorization);
        var own = b.Query.TrimStart('?');
        var added = string.Join('&', q.Select(kv => $"{FormEncode(kv.Key)}={FormEncode(kv.Value)}"));
        b.Query = own.Length > 0 ? own + "&" + added : added;
        return b.Uri;
    }

    /// <summary>
    /// One name or value of an application/x-www-form-urlencoded query, as Rust's <c>form_urlencoded::byte_serialize</c> writes it: A-Z a-z 0-9
    /// and <c>* - . _</c> stay, a space becomes <c>+</c>, every other UTF-8 byte is <c>%XX</c> (upper-case hex).
    /// </summary>
    public static string FormEncode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'*' or (byte)'-' or (byte)'.' or (byte)'_')
            {
                sb.Append((char)b);
            }
            else if (b == (byte)' ')
            {
                sb.Append('+');
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static async Task SwallowAsync(Task<Result<string>> wait)
    {
        try
        {
#pragma warning disable VSTHRD003 // the wait was started by SignInAsync on the same flow; this only observes its cancellation
            _ = await wait.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // the sign-in was abandoned
        }
    }
}
