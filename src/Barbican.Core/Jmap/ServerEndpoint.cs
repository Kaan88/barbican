// Mirrors: Rust jmap.rs:55 normalize_base (trim, no trailing slash, no /.well-known/jmap suffix, https:// when no scheme), :73
// fetch_session; RFC 8620 §2.2 (service autodiscovery at /.well-known/jmap). Owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>The server a client connects to (the base URL the user entered).</summary>
public sealed record ServerEndpoint(Uri BaseUri)
{
    /// <summary>The discovery URL: <c>{base}/.well-known/jmap</c>.</summary>
    public Uri DiscoveryUri => new($"{BaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/.well-known/jmap");

    /// <summary>What the user typed, normalised as Rust's normalize_base; null when it is not an http(s) URL.</summary>
    public static ServerEndpoint? Parse(string server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var b = server.Trim().TrimEnd('/');
        var wk = b.IndexOf("/.well-known/jmap", StringComparison.OrdinalIgnoreCase);
        if (wk >= 0)
        {
            b = b[..wk];
        }

        if (!b.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            b = "https://" + b;
        }

        return Uri.TryCreate(b, UriKind.Absolute, out var uri) && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            ? new ServerEndpoint(uri)
            : null;
    }
}
