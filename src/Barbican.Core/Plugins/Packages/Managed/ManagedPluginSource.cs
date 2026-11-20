// Bulwark app/api/auth/stalwart-context/route.ts, app/api/plugins/route.ts and bundle-fetch.ts.
using System.Security.Cryptography;
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Applies explicit provider and public-key approval to one signed-in OAuth login.</summary>
public sealed class ManagedPluginSource : IDisposable
{
    private readonly IAccountSessionSnapshots _accounts;
    private readonly ManagedPluginTransport _transport;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);
    private bool _disposed;

    private sealed record Route(Uri Origin, Uri Server, string PinnedKey, WeakReference<IAccountSession> Owner)
    {
        internal bool Owns(IAccountSession session) => Owner.TryGetTarget(out var owner) && ReferenceEquals(owner, session);
    }

    public ManagedPluginSource(IAccountSessionSnapshots accounts) : this(accounts, new ManagedPluginTransport()) { }

    internal ManagedPluginSource(IAccountSessionSnapshots accounts, ManagedPluginTransport transport)
    {
        _accounts = accounts;
        _transport = transport;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _routes.Clear();
        }
        _transport.Dispose();
    }

    internal bool ApproveProvider(AccountSessionSnapshot selected, Uri provider, string pinnedKey)
        => ApproveRoute(selected, provider, pinnedKey) is not null;

    private Route? ApproveRoute(AccountSessionSnapshot selected, Uri provider, string pinnedKey)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.ActiveSelectionGeneration is null || !OrganizationPolicyService.IsProviderOrigin(provider) ||
            !ManagedBundleVerifier.IsValidKey(pinnedKey) || !OAuthClient(selected, out var client)) return null;
        var pinned = client.Session;
        if (!Uri.TryCreate(pinned.ServerUrl, UriKind.Absolute, out var server) || !ValidServer(server)) return null;
        Route? route = null;
        return _accounts.TryRunWhileCurrent([selected], () =>
            client.TryRunWhileSessionCurrent(pinned, () => client.Auth.TryRunWhileOpen(() =>
            {
                lock (_gate)
                {
                    if (_disposed) return false;
                    var key = selected.Session.Account.Key;
                    var existing = _routes.GetValueOrDefault(key);
                    route = existing is not null && existing.Owns(selected.Session) &&
                        string.Equals(existing.Origin.AbsoluteUri, provider.AbsoluteUri, StringComparison.Ordinal) &&
                        string.Equals(existing.Server.AbsoluteUri, server.AbsoluteUri, StringComparison.Ordinal) &&
                        string.Equals(existing.PinnedKey, pinnedKey, StringComparison.Ordinal)
                        ? existing : new Route(provider, server, pinnedKey, new(selected.Session));
                    _routes[key] = route;
                    return true;
                }
            }))) ? route : null;
    }

    internal Task<Result<ManagedPluginCandidate>> AcquireForReviewAsync(AccountSessionSnapshot selected, Uri provider, string pinnedKey,
        string pluginId, string compatibilityVersion, Func<Func<bool>, bool> hostFence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var route = ApproveRoute(selected, provider, pinnedKey);
        return route is null ? Task.FromResult(Refused()) : FetchFromRouteAsync(route, selected, pluginId, compatibilityVersion, hostFence, ct);
    }

    internal bool Disconnect(AccountSessionSnapshot selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.ActiveSelectionGeneration is null) return false;
        var key = selected.Session.Account.Key;
        return _accounts.TryRunWhileCurrent([selected], () =>
        {
            lock (_gate) return _routes.GetValueOrDefault(key)?.Owns(selected.Session) == true && _routes.Remove(key);
        });
    }

    internal async Task<Result<ManagedPluginCandidate>> FetchAsync(string localAccountKey, string pluginId,
        string compatibilityVersion, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        ct.ThrowIfCancellationRequested();
        Route? route;
        lock (_gate) route = _routes.GetValueOrDefault(localAccountKey);
        if (route is null || _accounts.CaptureOwnerSession(localAccountKey).Value is not { } owner) return Refused();
        return await FetchFromRouteAsync(route, owner, pluginId, compatibilityVersion, static issue => issue(), ct).ConfigureAwait(false);
    }

    internal Task<Result<ManagedPluginCandidate>> AcquireForActivationAsync(string localAccountKey, Uri provider, string signerSha256,
        string pluginId, string compatibilityVersion, Func<Func<bool>, bool> hostFence, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(hostFence);
        ct.ThrowIfCancellationRequested();
        Route? route;
        lock (_gate) route = _routes.GetValueOrDefault(localAccountKey);
        if (route is null || !OrganizationPolicyService.IsProviderOrigin(provider) ||
            !string.Equals(route.Origin.AbsoluteUri, provider.AbsoluteUri, StringComparison.Ordinal) ||
            !string.Equals(Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(route.PinnedKey))), signerSha256, StringComparison.Ordinal) ||
            _accounts.CaptureOwnerSession(localAccountKey).Value is not { } owner || !route.Owns(owner.Session))
            return Task.FromResult(Refused());
        return FetchFromRouteAsync(route, owner, pluginId, compatibilityVersion, hostFence, ct);
    }
    private async Task<Result<ManagedPluginCandidate>> FetchFromRouteAsync(Route route, AccountSessionSnapshot owner, string pluginId,
        string compatibilityVersion, Func<Func<bool>, bool> hostFence, CancellationToken ct)
    {
        if (!route.Owns(owner.Session) || !OAuthClient(owner, out var client)) return Refused();
        var localAccountKey = owner.Session.Account.Key;
        var pinned = client.Session;
        if (!Uri.TryCreate(pinned.ServerUrl, UriKind.Absolute, out var currentServer) ||
            !string.Equals(route.Server.AbsoluteUri, currentServer.AbsoluteUri, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pinned.Username)) return Refused();

        bool RunUnderProvider(Func<bool> issue) => _accounts.TryRunWhileCurrent([owner], () =>
            client.TryRunWhileSessionCurrent(pinned, () => client.Auth.TryRunWhileOpen(() =>
        {
            lock (_gate)
                return ReferenceEquals(_routes.GetValueOrDefault(localAccountKey), route) && issue();
        })));
        bool Run(Func<bool> issue) => hostFence(() => RunUnderProvider(issue));
        ct.ThrowIfCancellationRequested();
        if (!Run(static () => true)) return Refused();
        var credential = await client.Auth.GetAsync(ct).ConfigureAwait(false);
        if (!credential.IsOk || credential.Value is not { Parameter: { } bearer } header ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)) return Refused();
        var artifact = await _transport.FetchAsync(route.Origin, route.Server, pinned.Username, bearer, pluginId, route.PinnedKey,
            compatibilityVersion, Run, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
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
