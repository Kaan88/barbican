// Bulwark app/api/auth/stalwart-context/route.ts, app/api/plugins/route.ts and bundle-fetch.ts.
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Applies explicit provider and public-key approval to one signed-in OAuth login.</summary>
internal sealed class ManagedPluginSource(IAccountSessionSnapshots accounts, ManagedPluginTransport transport)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);

    private sealed record Route(Uri Origin, Uri Server, string PinnedKey, WeakReference<IAccountSession> Owner)
    {
        internal bool Owns(IAccountSession session) => Owner.TryGetTarget(out var owner) && ReferenceEquals(owner, session);
    }

    internal bool ApproveProvider(AccountSessionSnapshot selected, Uri provider, string pinnedKey)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.ActiveSelectionGeneration is null || !OrganizationPolicyService.IsProviderOrigin(provider) ||
            !ManagedBundleVerifier.IsValidKey(pinnedKey) || !OAuthClient(selected, out var client)) return false;
        var pinned = client.Session;
        if (!Uri.TryCreate(pinned.ServerUrl, UriKind.Absolute, out var server) || !ValidServer(server)) return false;
        var route = new Route(provider, server, pinnedKey, new(selected.Session));
        lock (_gate) return accounts.TryRunWhileCurrent([selected], () =>
            client.TryRunWhileSessionCurrent(pinned, () => client.Auth.TryRunWhileOpen(() =>
            {
                _routes[selected.Session.Account.Key] = route;
                return true;
            })));
    }

    internal bool Disconnect(AccountSessionSnapshot selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.ActiveSelectionGeneration is null) return false;
        var key = selected.Session.Account.Key;
        lock (_gate) return accounts.TryRunWhileCurrent([selected], () =>
            _routes.GetValueOrDefault(key)?.Owns(selected.Session) == true && _routes.Remove(key));
    }

    internal async Task<Result<ManagedPluginCandidate>> FetchAsync(string localAccountKey, string pluginId,
        string compatibilityVersion, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        ct.ThrowIfCancellationRequested();
        Route? route;
        lock (_gate) route = _routes.GetValueOrDefault(localAccountKey);
        if (route is null || accounts.CaptureOwnerSession(localAccountKey).Value is not { } owner ||
            !route.Owns(owner.Session) || !OAuthClient(owner, out var client)) return Refused();
        var pinned = client.Session;
        if (!Uri.TryCreate(pinned.ServerUrl, UriKind.Absolute, out var currentServer) ||
            !string.Equals(route.Server.AbsoluteUri, currentServer.AbsoluteUri, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pinned.Username)) return Refused();

        bool Run(Func<bool> issue)
        {
            lock (_gate)
                return ReferenceEquals(_routes.GetValueOrDefault(localAccountKey), route) &&
                    accounts.TryRunWhileCurrent([owner], () => client.TryRunWhileSessionCurrent(pinned, () => client.Auth.TryRunWhileOpen(issue)));
        }
        if (!Run(static () => true)) return Refused();
        var credential = await client.Auth.GetAsync(ct).ConfigureAwait(false);
        if (!credential.IsOk || credential.Value is not { Parameter: { } bearer } header ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)) return Refused();
        var artifact = await transport.FetchAsync(route.Origin, route.Server, pinned.Username, bearer, pluginId, route.PinnedKey,
            compatibilityVersion, Run, ct).ConfigureAwait(false);
        if (!Run(static () => true)) return Refused();
        return artifact.IsOk
            ? Result<ManagedPluginCandidate>.Ok(new(artifact.Value!, route.Origin, localAccountKey, Run))
            : Result<ManagedPluginCandidate>.Fail(artifact.Error!);
    }

    private static bool OAuthClient(AccountSessionSnapshot capture, out JmapClient client)
    {
        client = null!;
        if (capture.Session.Client is not JmapClient jmap || jmap.Auth.IsClosed ||
            jmap.Auth.Inner is not OAuthTokenSource oauth ||
            !string.Equals(oauth.AccountKey, capture.Session.Account.Key, StringComparison.Ordinal))
            return false;
        client = jmap;
        return true;
    }

    private static bool ValidServer(Uri server) => server.IsAbsoluteUri && server.UserInfo.Length == 0 &&
        server.Query.Length == 0 && server.Fragment.Length == 0 && string.Equals(server.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

    private static Result<ManagedPluginCandidate> Refused() =>
        Result<ManagedPluginCandidate>.Fail(new ValidationError("Managed add-on provider approval is unavailable for this login."));
}
