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
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);

    /// <summary>Registers a provider the host explicitly trusts for this login, until sign-out or replacement.</summary>
    public bool ConfigureProvider(string localAccountKey, Uri jmapServer, Uri policyOrigin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        if (!ValidAddress(jmapServer) || !ValidAddress(policyOrigin) || policyOrigin.AbsolutePath.Length != 1) return false;
        var captured = accounts.CaptureOwnerSession(localAccountKey).Value;
        if (captured is null || !string.Equals(captured.Session.Account.Key, localAccountKey, StringComparison.Ordinal)) return false;
        lock (_gate)
        {
            return accounts.TryRunWhileCurrent([captured], () =>
            {
                _routes[localAccountKey] = new(jmapServer, policyOrigin, captured.Session);
                return true;
            });
        }
    }

    /// <summary>Revokes the provider route and invalidates policy reads issued under that configuration.</summary>
    public void RemoveProvider(string localAccountKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        lock (_gate) _ = _routes.Remove(localAccountKey);
    }

    public async Task<OrganizationPolicyLookup> ReadAsync(string localAccountKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        Route? route;
        lock (_gate) _routes.TryGetValue(localAccountKey, out route);
        if (route is null) return Status(OrganizationPolicyTransportStatus.Missing);

        var captured = accounts.CaptureOwnerSession(localAccountKey).Value;
        if (captured is null || !ReferenceEquals(captured.Session, route.Owner) ||
            captured.Session.Client is not JmapClient client) return Status(OrganizationPolicyTransportStatus.Stale);
        var pinned = client.Session;
        if (!Uri.TryCreate(pinned.ServerUrl, UriKind.Absolute, out var server) || !SameServer(route.JmapServer, server))
            return Status(OrganizationPolicyTransportStatus.Untrusted);
        if (client.Auth.IsClosed || client.Auth.Inner is not OAuthTokenSource oauth || !string.Equals(oauth.AccountKey, localAccountKey, StringComparison.Ordinal))
            return Status(OrganizationPolicyTransportStatus.Unauthorized);

        var read = new ReadContext(this, localAccountKey, route, captured, client, pinned);
        return await ReadPolicyAsync(read, ct).ConfigureAwait(false);
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
        return read.Run(static () => true) ? new(OrganizationPolicyTransportStatus.Authoritative, snapshot) : Status(OrganizationPolicyTransportStatus.Stale);
    }

    private static OrganizationPolicyLookup Status(OrganizationPolicyTransportStatus status) => new(status);

    private static bool ValidAddress(Uri? value) => value is { IsAbsoluteUri: true, UserInfo.Length: 0, Query.Length: 0, Fragment.Length: 0 }
        && string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && value.Host.Length > 0;

    private static bool SameServer(Uri configured, Uri actual) => ValidAddress(actual) &&
        string.Equals(configured.AbsoluteUri, actual.AbsoluteUri, StringComparison.Ordinal);

    private sealed record Route(Uri JmapServer, Uri PolicyOrigin, IAccountSession Owner);

    private sealed record ReadContext(OrganizationPolicyService Service, string LocalAccountKey, Route Route,
        AccountSessionSnapshot Captured, JmapClient Client, JmapSession Pinned)
    {
        public bool Run(Func<bool> issue)
        {
            lock (Service._gate)
            {
                return ReferenceEquals(Service._routes.GetValueOrDefault(LocalAccountKey), Route) &&
                    Service.RunForSession(Captured, Client, Pinned, issue);
            }
        }
    }

    private bool RunForSession(AccountSessionSnapshot captured, JmapClient client, JmapSession pinned, Func<bool> issue) =>
        accounts.TryRunWhileCurrent([captured], () => client.TryRunWhileSessionCurrent(pinned, () => client.Auth.TryRunWhileOpen(issue)));
}
