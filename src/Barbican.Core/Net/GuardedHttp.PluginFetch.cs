using System.Net;
using System.Net.Http.Headers;

namespace Barbican.Core.Net;

public sealed partial class GuardedHttp
{
    internal const int PluginBodyLimit = 25 * 1024 * 1024;
    private static readonly HttpRequestOptionsKey<Func<bool>> PluginConnectPolicy = new("barbican.addon.network-policy");

    public static GuardedHttp ForPluginFetch() => new(false, SystemResolveAsync, TimeProvider.System, PluginBodyLimit,
        TimeSpan.FromSeconds(30), DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli);

    internal async Task<PluginFetchReply?> SendPluginHopAsync(PluginFetchRequest request, CancellationToken ct)
    {
        if (!request.Uri.IsAbsoluteUri || request.Uri.UserInfo.Length != 0 ||
            !string.Equals(request.Uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || request.CanConnect?.Invoke() == false)
            return null;
        using var message = new HttpRequestMessage(request.Method, request.Uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        if (request.CanConnect is { } policy) message.Options.Set(PluginConnectPolicy, policy);
        if (request.Body is { } body) message.Content = new ByteArrayContent(body);
        foreach (var (name, value) in request.Headers)
        {
            if (message.Headers.TryAddWithoutValidation(name, value)) continue;
            if (message.Content is null || !message.Content.Headers.TryAddWithoutValidation(name, value)) return null;
        }
        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var hasLocation = response.Headers.TryGetValues("Location", out var locations);
            if (status is 301 or 302 or 303 or 307 or 308 && hasLocation)
            {
                if (locations!.Take(2).Count() != 1 || response.Headers.Location is not { } location) return null;
                return new(status, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], location);
            }

            // The handler expands a single HTTP encoding. Refuse layered or unknown encodings.
            if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > _maxBodyBytes) return null;
            var bytes = await ReadCappedAsync(response.Content, ct).ConfigureAwait(false);
            return bytes is null ? null : new(status, response.ReasonPhrase ?? string.Empty, PluginHeaders(response), bytes);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> PluginHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in new HttpHeaders[] { response.Headers, response.Content.Headers })
        {
            foreach (var (name, values) in group.NonValidated)
            {
                var key = name.ToLowerInvariant();
                if (key is "set-cookie" or "set-cookie2" or "proxy-authenticate" or "proxy-authorization") continue;
                headers[key] = values.ToString();
            }
        }
        return headers;
    }
}
