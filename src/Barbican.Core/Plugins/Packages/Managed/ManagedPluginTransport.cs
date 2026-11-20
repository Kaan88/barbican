// Bulwark app/api/auth/stalwart-context/route.ts, app/api/plugins/route.ts, app/api/admin/plugins/[id]/bundle/route.ts.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Fetches a managed bundle using an explicitly approved, short-lived authenticated context.</summary>
internal sealed class ManagedPluginTransport : IDisposable
{
    private const int MaxRegistry = 2 * 1024 * 1024;
    private const int MaxBundle = 20 * 1024 * 1024;
    private readonly OrganizationPolicyHttp _http;
    private readonly TimeSpan _timeout;
    internal ManagedPluginTransport() { _http = new(); _timeout = TimeSpan.FromSeconds(15); }
    internal ManagedPluginTransport(HttpMessageHandler handler, TimeSpan? timeout = null) { _http = new(handler); _timeout = timeout ?? TimeSpan.FromSeconds(15); }

    internal async Task<Result<ManagedPluginArtifact>> FetchAsync(Uri provider, Uri jmapServer, string username, string token,
        string pluginId, string pinnedKey, string compatibility, Func<Func<bool>, bool> beforeSend, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(beforeSend);
        ct.ThrowIfCancellationRequested();
        if (!OrganizationPolicyService.IsProviderOrigin(provider) || !ValidServer(jmapServer) || !ValidInput(username, 320) || !ValidInput(token, 8192) ||
            !ValidId(pluginId) || token.Any(c => c is < '!' or > '~') || !ManagedBundleVerifier.IsValidKey(pinnedKey) ||
            !ValidInput(compatibility, 64)) return Fail();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            var cookie = await AuthenticateAsync(provider, jmapServer, username, token, beforeSend, timeout.Token).ConfigureAwait(false);
            if (cookie is null) return Fail();
            var registry = await RegistryAsync(provider, cookie, beforeSend, timeout.Token).ConfigureAwait(false);
            if (registry?.FindEligible(pluginId) is not { Tier: "privileged" }) return Fail();
            var bundle = await BundleAsync(provider, cookie, pluginId, beforeSend, timeout.Token).ConfigureAwait(false);
            if (bundle is null || !beforeSend(static () => true)) return Fail();
            var artifact = await ManagedPluginArtifactFactory.CreateAsync(registry, pluginId, bundle.Value.Bytes, pinnedKey, bundle.Value.Signature, compatibility, timeout.Token).ConfigureAwait(false);
            return !timeout.IsCancellationRequested && beforeSend(static () => true) ? artifact : Fail();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail(); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException) { return Fail(); }
    }

    private async Task<string?> AuthenticateAsync(Uri provider, Uri jmapServer, string username, string token, Func<Func<bool>, bool> fence, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(provider, "/api/auth/stalwart-context"));
        var json = new JsonObject
        {
            ["serverUrl"] = jmapServer.AbsoluteUri, ["username"] = username, ["authHeader"] = "Bearer " + token, ["slot"] = 0,
        }.ToJsonString();
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Origin", provider.GetLeftPart(UriPartial.Authority));
        using var response = await SendAsync(request, fence, ct).ConfigureAwait(false);
        if (response?.StatusCode != HttpStatusCode.OK || Cookie(response) is not { } cookie) return null;
        var bytes = await ReadAsync(response, 16384, ct).ConfigureAwait(false);
        if (bytes is null) return null;
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count(p => string.Equals(p.Name, "ok", StringComparison.Ordinal)) != 1 ||
            !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
        return cookie;
    }

    private async Task<ManagedPluginRegistry?> RegistryAsync(Uri provider, string cookie, Func<Func<bool>, bool> fence, CancellationToken ct)
    {
        using var request = Get(provider, "/api/plugins", cookie);
        using var response = await SendAsync(request, fence, ct).ConfigureAwait(false);
        if (response?.StatusCode != HttpStatusCode.OK) return null;
        var bytes = await ReadAsync(response, MaxRegistry, ct).ConfigureAwait(false);
        if (bytes is null) return null;
        using var document = JsonDocument.Parse(bytes);
        var parsed = ManagedPluginRegistry.Parse(document.RootElement);
        return parsed.IsOk ? parsed.Value : null;
    }

    private async Task<(byte[] Bytes, string Signature)?> BundleAsync(Uri provider, string cookie, string id, Func<Func<bool>, bool> fence, CancellationToken ct)
    {
        using var request = Get(provider, "/api/admin/plugins/" + id + "/bundle", cookie);
        using var response = await SendAsync(request, fence, ct).ConfigureAwait(false);
        if (response?.StatusCode != HttpStatusCode.OK || !response.Headers.TryGetValues("X-Bundle-Signature", out var values)) return null;
        var headers = values.ToArray();
        if (headers.Length != 1 || !ValidInput(headers[0], 512) || headers[0].Contains(',', StringComparison.Ordinal)) return null;
        var bytes = await ReadAsync(response, MaxBundle, ct).ConfigureAwait(false);
        return bytes is null ? null : (bytes, headers[0]);
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, Func<Func<bool>, bool> fence, CancellationToken ct)
    {
        Task<HttpResponseMessage>? pending = null;
        var admitted = fence(() =>
        {
            if (pending is not null || ct.IsCancellationRequested) return false;
            pending = _http.SendAsync(request, ct);
            return true;
        });
        if (!admitted || pending is null) { if (pending is not null) (await pending.ConfigureAwait(false)).Dispose(); return null; }
        return await pending.ConfigureAwait(false);
    }

    private static HttpRequestMessage Get(Uri provider, string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(provider, path));
        request.Headers.TryAddWithoutValidation("Cookie", "jmap_stalwart_ctx=" + cookie);
        return request;
    }

    private static async Task<byte[]?> ReadAsync(HttpResponseMessage response, int max, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } length && (length < 0 || length > max)) return null;
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > max) return null;
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static string? Cookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        string? found = null;
        foreach (var header in values)
        {
            if (header.Length > 8192 || header.Any(char.IsControl)) return null;
            var parts = header.Split(';', StringSplitOptions.TrimEntries);
            var pair = parts[0].Split('=', 2);
            if (pair.Length != 2 || !string.Equals(pair[0], "jmap_stalwart_ctx", StringComparison.Ordinal)) continue;
            if (found is not null || pair[1].Length is 0 or > 4096 || pair[1].Any(c => c is < '!' or > '~' or ',' or ';' or '"' or '\\')) return null;
            var attrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Skip(1))
            {
                var attr = part.Split('=', 2);
                if (!attrs.Add(attr[0]) || string.Equals(attr[0], "domain", StringComparison.OrdinalIgnoreCase)) return null;
                if (string.Equals(attr[0], "path", StringComparison.OrdinalIgnoreCase) &&
                    (attr.Length != 2 || !string.Equals(attr[1], "/", StringComparison.Ordinal))) return null;
                if ((string.Equals(attr[0], "secure", StringComparison.OrdinalIgnoreCase) || string.Equals(attr[0], "httponly", StringComparison.OrdinalIgnoreCase)) && attr.Length != 1) return null;
                if (string.Equals(attr[0], "max-age", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(attr[0], "expires", StringComparison.OrdinalIgnoreCase)) return null;
            }
            if (!attrs.Contains("path") || !attrs.Contains("secure") || !attrs.Contains("httponly")) return null;
            found = pair[1];
        }
        return found;
    }

    private static bool ValidServer(Uri value) => value is { IsAbsoluteUri: true } && string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
        string.IsNullOrEmpty(value.UserInfo) && string.IsNullOrEmpty(value.Query) && string.IsNullOrEmpty(value.Fragment);
    private static bool ValidInput(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    private static bool ValidId(string value) => value.Length is >= 2 and <= 128 && char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]) &&
        value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'z' || c == '-');
    private static Result<ManagedPluginArtifact> Fail() => Result<ManagedPluginArtifact>.Fail(new ValidationError("Managed add-on could not be verified."));
    public void Dispose() => _http.Dispose();
}
