// Mirrors Bulwark app/api/admin/policy/route.ts:46-147 and lib/auth/bearer-identity.ts:56-57, 101-121.
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Reads policy only from a caller-configured trusted HTTPS origin, bound to a captured JMAP server.</summary>
public sealed class OrganizationPolicyTransport : IDisposable
{
    private const int MaxBytes = 262144;
    private readonly OrganizationPolicyHttp _client;
    private readonly TimeSpan _timeout;

    /// <summary>Creates a client without redirects, cookies, proxy use, or ambient credentials.</summary>
    public OrganizationPolicyTransport() { _client = new OrganizationPolicyHttp(); _timeout = TimeSpan.FromSeconds(15); }

    internal OrganizationPolicyTransport(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _client = new OrganizationPolicyHttp(handler);
    }

    /// <summary>The caller supplies a trusted policy origin and the captured account's JMAP URL, plus its OAuth access token.</summary>
    public async Task<OrganizationPolicyTransportResult> ReadAsync(Uri trustedPolicyOrigin, Uri jmapServer, string oauthBearer, Func<Func<bool>, bool> beforeSend, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(beforeSend);
        ct.ThrowIfCancellationRequested();
        if (!ValidHttps(trustedPolicyOrigin) || !string.Equals(trustedPolicyOrigin.AbsolutePath, "/", StringComparison.Ordinal) || trustedPolicyOrigin.Query.Length != 0 || trustedPolicyOrigin.Fragment.Length != 0 ||
            !ValidHttps(jmapServer) || jmapServer.Query.Length != 0 || jmapServer.Fragment.Length != 0 || !ValidBearer(oauthBearer)) return Status(OrganizationPolicyTransportStatus.Untrusted);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(trustedPolicyOrigin, "/api/admin/policy"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthBearer);
        request.Headers.TryAddWithoutValidation("X-JMAP-Server-Url", jmapServer.AbsoluteUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        var readToken = timeout.Token;
        try
        {
            Task<HttpResponseMessage>? pending = null;
            var admitted = beforeSend(() =>
            {
                if (pending is not null || readToken.IsCancellationRequested) return false;
                pending = _client.SendAsync(request, readToken);
                return true;
            });
            ct.ThrowIfCancellationRequested();
            if (!admitted || pending is null) return Status(OrganizationPolicyTransportStatus.Stale);
            using var response = await pending.ConfigureAwait(false);
            return await ReadResponseAsync(response, readToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Status(OrganizationPolicyTransportStatus.Unavailable); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
        { return Status(ex is JsonException ? OrganizationPolicyTransportStatus.Malformed : OrganizationPolicyTransportStatus.Unavailable); }
    }

    private static async Task<OrganizationPolicyTransportResult> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return Status(OrganizationPolicyTransportStatus.Unauthorized);
        if (response.StatusCode == HttpStatusCode.NotFound) return Status(OrganizationPolicyTransportStatus.Missing);
        if (response.StatusCode != HttpStatusCode.OK) return Status(OrganizationPolicyTransportStatus.Unavailable);
        if (response.Headers.TryGetValues("X-Bulwark-Policy-Scope", out var scope))
        {
            var scopes = scope.ToArray();
            return Status(scopes.Length == 1 && string.Equals(scopes[0].Trim(), "public", StringComparison.OrdinalIgnoreCase)
                ? OrganizationPolicyTransportStatus.Public : OrganizationPolicyTransportStatus.Untrusted);
        }
        if (!response.Headers.TryGetValues("X-Bulwark-Policy-Domains", out var values)) return Status(OrganizationPolicyTransportStatus.Untrusted);
        var domainHeaders = values.ToArray();
        if (domainHeaders.Length != 1 || domainHeaders[0].Length > 8192) return Status(OrganizationPolicyTransportStatus.Malformed);
        if (string.IsNullOrWhiteSpace(domainHeaders[0])) return Status(OrganizationPolicyTransportStatus.Untrusted);
        var rawDomains = domainHeaders[0].Split(',', StringSplitOptions.TrimEntries);
        if (rawDomains.Length > 200) return Status(OrganizationPolicyTransportStatus.Malformed);
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var idn = new System.Globalization.IdnMapping { UseStd3AsciiRules = true };
        foreach (var rawDomain in rawDomains)
        {
            if (rawDomain.Length == 0 || rawDomain.EndsWith('.')) return Status(OrganizationPolicyTransportStatus.Malformed);
            string domain;
            try { domain = idn.GetAscii(rawDomain).ToLowerInvariant(); }
            catch (ArgumentException) { return Status(OrganizationPolicyTransportStatus.Malformed); }
            if (domain.Length > 253 || Uri.CheckHostName(domain) != UriHostNameType.Dns) return Status(OrganizationPolicyTransportStatus.Malformed);
            if (canonical.TryGetValue(domain, out var original) && !string.Equals(original, rawDomain, StringComparison.OrdinalIgnoreCase))
                return Status(OrganizationPolicyTransportStatus.Malformed);
            canonical[domain] = rawDomain;
        }
        var domains = canonical.Keys.ToArray();
        return await ReadBodyAsync(response, domains, ct).ConfigureAwait(false);
    }

    private static async Task<OrganizationPolicyTransportResult> ReadBodyAsync(HttpResponseMessage response, string[] domains, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > MaxBytes) return Status(OrganizationPolicyTransportStatus.Malformed);
        var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var inputLifetime = input.ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (body.Length + read > MaxBytes) return Status(OrganizationPolicyTransportStatus.Malformed);
            await body.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
        return json.RootElement.ValueKind == JsonValueKind.Object
            ? new(OrganizationPolicyTransportStatus.Authoritative, json.RootElement.Clone(), Array.AsReadOnly(domains)) : Status(OrganizationPolicyTransportStatus.Malformed);
    }

    private static bool ValidHttps(Uri? uri) => uri is { IsAbsoluteUri: true } && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && uri.UserInfo.Length == 0 && uri.Host.Length > 0;
    private static bool ValidBearer(string? token) => token is { Length: >= 16 and <= 8192 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or '+' or '/' or '=') && !token.TrimEnd('=').Contains('=', StringComparison.Ordinal);
    private static OrganizationPolicyTransportResult Status(OrganizationPolicyTransportStatus status) => new(status);
    public void Dispose() => _client.Dispose();
}
