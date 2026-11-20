// Account and provider boundary for Bulwark app/api/admin/policy/route.ts.
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Host-owned trust and policy reads for a signed-in OAuth account.</summary>
public sealed class OrganizationPolicyService(IAccountSessionSnapshots accounts, OrganizationPolicyTransport transport)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RouteState> _routes = new(StringComparer.Ordinal);

    /// <summary>Registers a provider the host explicitly trusts for this login, until sign-out or replacement.</summary>
    public bool ConfigureProvider(string localAccountKey, Uri jmapServer, Uri policyOrigin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        if (!ValidAddress(jmapServer) || !IsProviderOrigin(policyOrigin)) return false;
        var captured = accounts.CaptureOwnerSession(localAccountKey).Value;
        if (captured is null || !string.Equals(captured.Session.Account.Key, localAccountKey, StringComparison.Ordinal)) return false;
        lock (_gate) return accounts.TryRunWhileCurrent([captured], () => StoreRoute(captured, jmapServer, policyOrigin));
    }

    /// <summary>Checks whether the exact selected login can share its OAuth access token with a policy provider.</summary>
    public bool CanConfigureProvider(AccountSessionSnapshot captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        return TryGetEnrollment(captured, out _, out _, out _) && accounts.IsSessionCurrent(captured);
    }

    /// <summary>Stores the approved HTTPS origin for the exact selected OAuth login.</summary>
    public bool ConfigureProvider(AccountSessionSnapshot captured, Uri policyOrigin)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (!IsProviderOrigin(policyOrigin) || !TryGetEnrollment(captured, out var client, out var pinned, out var server)) return false;
        lock (_gate)
        {
            return RunForSession(captured, client, pinned, () => StoreRoute(captured, server, policyOrigin));
        }
    }

    /// <summary>Checks a consented candidate privately, replacing the current route only after an authoritative response.</summary>
    public Task<OrganizationPolicyLookup> VerifyProviderAsync(AccountSessionSnapshot captured, Uri policyOrigin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ct.ThrowIfCancellationRequested();
        if (!IsProviderOrigin(policyOrigin)) return Task.FromResult(Status(OrganizationPolicyTransportStatus.Untrusted));
        if (!TryGetEnrollment(captured, out var client, out var pinned, out var server))
            return Task.FromResult(Status(OrganizationPolicyTransportStatus.Stale));
        var key = captured.Session.Account.Key;
        ReadContext verify;
        lock (_gate)
        {
            var candidate = new Route(server, policyOrigin, captured.Session);
            verify = new ReadContext(this, key, candidate, captured, client, pinned, _routes.GetValueOrDefault(key), IsVerification: true);
        }

        return ReadPolicyAsync(verify, ct);
    }

    /// <summary>Reads the configured origin while the exact account selection is current.</summary>
    public Uri? ProviderFor(AccountSessionSnapshot captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        lock (_gate)
        {
            return captured.ActiveSelectionGeneration is not null && accounts.IsSessionCurrent(captured) &&
                RouteFor(captured) is { } route ? route.PolicyOrigin : null;
        }
    }

    /// <summary>Revokes the provider route and invalidates policy reads issued under that configuration.</summary>
    public void RemoveProvider(string localAccountKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        lock (_gate) _routes[localAccountKey] = new(Route: null);
    }

    /// <summary>Disconnects only the exact selected login's provider.</summary>
    public bool RemoveProvider(AccountSessionSnapshot captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        lock (_gate)
        {
            return captured.ActiveSelectionGeneration is not null && accounts.TryRunWhileCurrent([captured], () =>
            {
                var removed = RouteFor(captured) is not null;
                _routes[captured.Session.Account.Key] = new(Route: null);
                return removed;
            });
        }
    }

    public async Task<OrganizationPolicyLookup> ReadAsync(string localAccountKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        return await ReadAsync(localAccountKey, selection: null, ct).ConfigureAwait(false);
    }

    /// <summary>Checks policy from Settings under the selection that authorized this request.</summary>
    public Task<OrganizationPolicyLookup> ReadAsync(AccountSessionSnapshot captured, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(captured);
        return captured.ActiveSelectionGeneration is null
            ? Task.FromResult(Status(OrganizationPolicyTransportStatus.Stale))
            : ReadAsync(captured.Session.Account.Key, captured, ct);
    }

    private async Task<OrganizationPolicyLookup> ReadAsync(string localAccountKey, AccountSessionSnapshot? selection, CancellationToken ct)
    {
        Route? route;
        lock (_gate) route = _routes.GetValueOrDefault(localAccountKey)?.Route;
        if (route is null) return Status(OrganizationPolicyTransportStatus.Missing);

        var captured = selection ?? accounts.CaptureOwnerSession(localAccountKey).Value;
        if (captured is null || !route.BelongsTo(captured.Session) ||
            captured.Session.Client is not JmapClient client) return Status(OrganizationPolicyTransportStatus.Stale);
        var pinned = client.Session;
        if (!Uri.TryCreate(pinned.ServerUrl, UriKind.Absolute, out var server) || !SameServer(route.JmapServer, server))
            return Status(OrganizationPolicyTransportStatus.Untrusted);
        if (client.Auth.IsClosed || client.Auth.Inner is not OAuthTokenSource oauth || !string.Equals(oauth.AccountKey, localAccountKey, StringComparison.Ordinal))
            return Status(OrganizationPolicyTransportStatus.Unauthorized);

        var read = new ReadContext(this, localAccountKey, route, captured, client, pinned);
        return await ReadPolicyAsync(read, ct).ConfigureAwait(false);
    }

    private static bool TryGetEnrollment(AccountSessionSnapshot captured, out JmapClient client, out JmapSession pinned, out Uri server)
    {
        client = null!;
        pinned = null!;
        server = null!;
        if (captured.ActiveSelectionGeneration is null || captured.Session.Client is not JmapClient jmap || jmap.Auth.IsClosed ||
            jmap.Auth.Inner is not OAuthTokenSource oauth ||
            !string.Equals(oauth.AccountKey, captured.Session.Account.Key, StringComparison.Ordinal)) return false;
        var session = jmap.Session;
        if (!Uri.TryCreate(session.ServerUrl, UriKind.Absolute, out var uri) || !ValidAddress(uri)) return false;
        client = jmap;
        pinned = session;
        server = uri;
        return true;
    }

    private async Task<OrganizationPolicyLookup> ReadPolicyAsync(ReadContext read, CancellationToken ct)
    {
        if (!read.Run(static () => true)) return Status(OrganizationPolicyTransportStatus.Stale);
        var subject = await OrganizationPolicySubjectSource.ReadAsync(read.Client, read.Pinned, read.Run, ct).ConfigureAwait(false);
        if (!read.Run(static () => true)) return Status(OrganizationPolicyTransportStatus.Stale);
        if (subject is null) return Status(OrganizationPolicyTransportStatus.Unavailable);

        var auth = await read.Client.Auth.GetAsync(ct).ConfigureAwait(false);
        if (!read.Run(static () => true)) return Status(OrganizationPolicyTransportStatus.Stale);
        if (!auth.IsOk) return Status(OrganizationPolicyTransportStatus.Unavailable);
        if (auth.Value is not { Parameter: { } bearer } header || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            return Status(OrganizationPolicyTransportStatus.Unauthorized);

        var response = await transport.ReadAsync(read.Route.PolicyOrigin, read.Route.JmapServer, bearer, read.Run, ct).ConfigureAwait(false);
        if (!read.Run(static () => true)) return Status(OrganizationPolicyTransportStatus.Stale);
        if (response.Status != OrganizationPolicyTransportStatus.Authoritative) return Status(response.Status);
        if (response.Policy is not { } policy) return Status(OrganizationPolicyTransportStatus.Malformed);
        if (!OrganizationPolicySnapshot.Covers(response.Domains, subject.Email) ||
            subject.Groups.Any(group => !OrganizationPolicySnapshot.Covers(response.Domains, group)))
            return Status(OrganizationPolicyTransportStatus.Untrusted);

        var parsed = OrganizationPolicyParser.Parse(policy);
        if (!parsed.IsOk) return Status(OrganizationPolicyTransportStatus.Malformed);
        var snapshot = new OrganizationPolicySnapshot(read.LocalAccountKey, parsed.Value!, subject, response.Domains, read.Run);
        return read.Finish(ct) ? new(OrganizationPolicyTransportStatus.Authoritative, snapshot) : Status(OrganizationPolicyTransportStatus.Stale);
    }

    private static OrganizationPolicyLookup Status(OrganizationPolicyTransportStatus status) => new(status);

    private static bool ValidAddress(Uri? value) => value is { IsAbsoluteUri: true, UserInfo.Length: 0, Query.Length: 0, Fragment.Length: 0 }
        && string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && value.Host.Length > 0;

    /// <summary>Checks for an HTTPS authority without credentials, a path, query or fragment.</summary>
    public static bool IsProviderOrigin(Uri? value) => ValidAddress(value) &&
        string.Equals(value!.AbsolutePath, "/", StringComparison.Ordinal);

    private static bool SameServer(Uri configured, Uri actual) => ValidAddress(actual) &&
        string.Equals(configured.AbsoluteUri, actual.AbsoluteUri, StringComparison.Ordinal);

    private sealed class Route(Uri jmapServer, Uri policyOrigin, IAccountSession owner)
    {
        private readonly WeakReference<IAccountSession> _owner = new(owner);
        public Uri JmapServer { get; } = jmapServer;
        public Uri PolicyOrigin { get; } = policyOrigin;
        public bool BelongsTo(IAccountSession session) => _owner.TryGetTarget(out var owner) && ReferenceEquals(owner, session);
    }
    private sealed record RouteState(Route? Route);

    private bool StoreRoute(AccountSessionSnapshot captured, Uri server, Uri origin)
    {
        _routes[captured.Session.Account.Key] = new(new Route(server, origin, captured.Session));
        return true;
    }

    private Route? RouteFor(AccountSessionSnapshot captured) =>
        _routes.GetValueOrDefault(captured.Session.Account.Key)?.Route is { } route && route.BelongsTo(captured.Session) ? route : null;

    private sealed record ReadContext(OrganizationPolicyService Service, string LocalAccountKey, Route Route,
        AccountSessionSnapshot Captured, JmapClient Client, JmapSession Pinned, RouteState? PreviousRoute = null, bool IsVerification = false)
    {
        private bool _pending = IsVerification;

        public bool Finish(CancellationToken ct) => Run(() =>
        {
            if (ct.IsCancellationRequested) return false;
            if (_pending)
            {
                Service._routes[LocalAccountKey] = new(Route);
                _pending = false;
            }
            return true;
        });

        public bool Run(Func<bool> issue)
        {
            lock (Service._gate)
            {
                var current = Service._routes.GetValueOrDefault(LocalAccountKey);
                var routeIsCurrent = _pending ? ReferenceEquals(current, PreviousRoute) : ReferenceEquals(current?.Route, Route);
                return routeIsCurrent &&
                    Service.RunForSession(Captured, Client, Pinned, issue);
            }
        }
    }

    private bool RunForSession(AccountSessionSnapshot captured, JmapClient client, JmapSession pinned, Func<bool> issue) =>
        accounts.TryRunWhileCurrent([captured], () => client.TryRunWhileSessionCurrent(pinned, () => client.Auth.TryRunWhileOpen(issue)));
}
