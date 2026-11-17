// Mirrors: Rust oauth.rs:87 discover, :73 meta_from, :50 rebase, :61 pick_scope, :13 WANTED (at rust-final); spec B-OAUTH-002..005.
// RFC 8414 §3 (/.well-known/oauth-authorization-server), OIDC Discovery 1.0 §4 (/.well-known/openid-configuration); Stalwart 0.16
// crates/http/src/auth/oauth/auth.rs:141 handle_discover_request (/api/discover/<account>: the OIDC document of the account's directory,
// else the server's own). Bulwark lib/oauth/discovery.ts:295 asks the same two well-known documents; lib/oauth/scopes.ts:61 also adds every
// urn:ietf:params:oauth:scope:* data scope the provider lists.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Auth;

/// <summary>Finds the OAuth endpoints of a server or an account.</summary>
public sealed class OAuthDiscovery(OAuthHttp http)
{
    /// <summary>The prefix of the OAuth data scopes Stalwart offers (mail, contacts, calendars, files).</summary>
    public const string DataScopePrefix = "urn:ietf:params:oauth:scope:";

    /// <summary>The scopes asked for, when the provider lists them (Rust oauth.rs:14).</summary>
    public static readonly IReadOnlyList<string> Wanted = ["openid", "email", "profile", "offline_access"];

    /// <summary>
    /// The metadata for <paramref name="account"/> at <paramref name="baseUri"/>: Stalwart's per-account discovery when an account is given and
    /// it names an authorization endpoint, else the server's own metadata.
    /// </summary>
    public async Task<Result<OAuthMetadata>> DiscoverAsync(Uri baseUri, string account, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        var root = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        JsonObject? own = null;
        foreach (var wk in (string[])["/.well-known/oauth-authorization-server", "/.well-known/openid-configuration"])
        {
            var r = await http.GetJsonAsync(new Uri(root + wk), ct).ConfigureAwait(false);
            if (r.IsSuccess && r.Json is not null)
            {
                own = r.Json;
                break;
            }
        }

        var name = (account ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            var r = await http.GetJsonAsync(new Uri($"{root}/api/discover/{Uri.EscapeDataString(name)}"), ct).ConfigureAwait(false);
            if (r.IsSuccess && r.Str("authorization_endpoint") is { } a)
            {
                var isOwn = a.StartsWith('/')
                    || (Uri.TryCreate(a, UriKind.Absolute, out var au) && SameOrigin(au, baseUri))
                    || (own?["authorization_endpoint"] is JsonValue ov && ov.TryGetValue<string>(out var os) && string.Equals(os, a, StringComparison.Ordinal));
                if (MetaFrom(r.Json!, baseUri, isOwn) is { } m)
                {
                    return Result<OAuthMetadata>.Ok(m);
                }
            }
        }

        return own is not null && MetaFrom(own, baseUri, own: true) is { } meta
            ? Result<OAuthMetadata>.Ok(meta)
            : Result<OAuthMetadata>.Fail(new AuthError("This server does not offer OAuth sign-in.", NeedsSignIn: false));
    }

    /// <summary>The metadata in a discovery document; endpoints of the server's own document are rebased onto the address the user typed.</summary>
    public static OAuthMetadata? MetaFrom(JsonObject doc, Uri baseUri, bool own)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(baseUri);
        string? Str(string k) => doc[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
        Uri? Fix(string? e) => e is null ? null : Absolute(own ? Rebase(e, baseUri) : e);
        var authorization = Fix(Str("authorization_endpoint"));
        var token = Fix(Str("token_endpoint"));
        if (authorization is null || token is null)
        {
            return null;
        }

        return new OAuthMetadata(authorization, token, Fix(Str("registration_endpoint")), Fix(Str("revocation_endpoint")), PickScope(doc["scopes_supported"]), External: !own);
    }

    /// <summary>
    /// An own-server endpoint on the origin the user reaches the server under (Stalwart may advertise its configured hostname); path and
    /// query are kept (Rust oauth.rs:50).
    /// </summary>
    public static string Rebase(string endpoint, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(baseUri);
        var origin = baseUri.GetLeftPart(UriPartial.Authority);
        if (endpoint.StartsWith('/') && !endpoint.StartsWith("//", StringComparison.Ordinal))
        {
            return origin + endpoint;
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var e) && (IsHttp(e))
            ? origin + e.AbsolutePath + e.Query
            : endpoint;
    }

    /// <summary>The scope: the wanted ones the provider lists plus every data scope it lists; the four defaults when it lists none.</summary>
    public static string PickScope(JsonNode? supported)
    {
        if (supported is not JsonArray list)
        {
            return string.Join(' ', Wanted);
        }

        var names = list.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList();
        var outList = Wanted.Where(names.Contains).ToList();
        outList.AddRange(names.Where(s => s.StartsWith(DataScopePrefix, StringComparison.Ordinal)));
        return string.Join(' ', outList.Distinct(StringComparer.Ordinal));
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.GetLeftPart(UriPartial.Authority), b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    // only http(s) endpoints are used: anything else in a discovery document is ignored
    private static Uri? Absolute(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && IsHttp(u) ? u : null;

    private static bool IsHttp(Uri u) =>
        string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(u.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal);
}
