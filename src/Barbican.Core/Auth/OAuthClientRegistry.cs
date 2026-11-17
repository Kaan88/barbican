// Mirrors: Rust oauth.rs:127 clients_path (oauth-clients.json), :132 client_id (RFC 7591 dynamic registration as a public native client,
// the id cached per registration endpoint, the configured id when registration is not offered or fails); RFC 8252 §8.4 (public clients);
// Stalwart crates/http/src/auth/oauth/registration.rs (POST /api/oauth/register, the answer's client_id). Bulwark lib/oauth/discovery.ts
// and stores/client-registry.ts register the same way. spec B-OAUTH-007. The cache lives in the profile's config dir (M1-plan §6.3), never
// in the Rust app's.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Core.Auth;

/// <summary>Client ids per authorization server: registered once, then cached.</summary>
public sealed class OAuthClientRegistry(OAuthHttp http, IProfilePaths paths) : IDisposable
{
    /// <summary>The cache file in the profile's config dir.</summary>
    public const string FileName = "oauth-clients.json";

    /// <summary>The client id shown in the sign-in form when nothing else is typed (Rust ui/app.slint:1746).</summary>
    public const string DefaultClientId = "barbican";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string CachePath => Path.Combine(paths.ConfigDir, FileName);

    /// <summary>The client id for <paramref name="meta"/>: cached, else registered, else <paramref name="fallback"/>.</summary>
    public async Task<string> ClientIdAsync(OAuthMetadata meta, Uri redirect, string fallback, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(redirect);
        ArgumentNullException.ThrowIfNull(fallback);
        if (meta.Registration is not { } reg)
        {
            return fallback;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = await Task.Run(ReadCache, ct).ConfigureAwait(false);
            if (cache[reg.AbsoluteUri] is JsonValue v && v.TryGetValue<string>(out var known) && known.Length > 0)
            {
                return known;
            }

            var body = new JsonObject
            {
                ["client_name"] = "Barbican",
                ["redirect_uris"] = new JsonArray(redirect.AbsoluteUri, "http://127.0.0.1:8791/callback", "http://127.0.0.1/callback"),
                ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
                ["response_types"] = new JsonArray("code"),
                ["token_endpoint_auth_method"] = "none",
                ["application_type"] = "native",
                ["scope"] = meta.Scope,
            };
            var r = await http.PostJsonAsync(reg, body, ct).ConfigureAwait(false);
            if (!r.IsSuccess || r.Str("client_id") is not { } id)
            {
                return fallback;
            }

            cache[reg.AbsoluteUri] = id;
            var text = cache.ToJsonString();
            await AtomicFile.WriteAllTextAsync(CachePath, text, ct).ConfigureAwait(false);
            return id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private JsonObject ReadCache()
    {
        try
        {
            return AtomicFile.ReadAllTextOrNull(CachePath) is { } text && JsonNode.Parse(text) is JsonObject o ? o : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
