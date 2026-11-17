// Mirrors: Rust jmap.rs:55 normalize_base (trim, no trailing slash, no /.well-known/jmap suffix, https:// when no scheme), :73
// fetch_session; RFC 8620 §2.2 (service autodiscovery at /.well-known/jmap). Owner jmap.
// User decision D-10 (Fable review 1): a plain http:// server sends the password in clear, so it is accepted only on this computer
// (localhost, 127.0.0.0/8, ::1); anywhere else the address is refused with a message that says why. Rust accepted any http:// server.
// Fable F-14: the scheme test is "http://" or "https://", so a host name that merely begins with "http" (httpbin.example) gets https://.

using System.Net;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Jmap;

/// <summary>The server a client connects to (the base URL the user entered).</summary>
public sealed record ServerEndpoint(Uri BaseUri)
{
    /// <summary>The refusal of a plain http server that is not on this computer.</summary>
    public const string InsecureMessage =
        "Barbican connects to mail servers over https only (plain http would send your password unencrypted). Use an https:// address; plain http works only for a server on this computer.";

    /// <summary>The discovery URL: <c>{base}/.well-known/jmap</c>.</summary>
    public Uri DiscoveryUri => new($"{BaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/.well-known/jmap");

    /// <summary>What the user typed, normalised as Rust's normalize_base; null when it is not a usable server address (see <see cref="Read"/>).</summary>
    public static ServerEndpoint? Parse(string server) => Read(server) is { IsOk: true } r ? r.Value : null;

    /// <summary>What the user typed, normalised; or why it cannot be used (empty, not an address, plain http to another computer).</summary>
    public static Result<ServerEndpoint> Read(string server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var b = server.Trim().TrimEnd('/');
        if (b.Length == 0)
        {
            return Fail("Enter your server address.");
        }

        var wk = b.IndexOf("/.well-known/jmap", StringComparison.OrdinalIgnoreCase);
        if (wk >= 0)
        {
            b = b[..wk];
        }

        if (!b.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !b.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            b = "https://" + b;
        }

        if (!Uri.TryCreate(b, UriKind.Absolute, out var uri) || uri.Host.Length == 0
            || !(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)))
        {
            return Fail("That is not a server address.");
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !IsLoopback(uri))
        {
            return Fail(InsecureMessage);
        }

        return Result<ServerEndpoint>.Ok(new ServerEndpoint(uri));
    }

    /// <summary>The host is this computer: <c>localhost</c> or a loopback address (RFC 6761 §6.3, RFC 1122 §3.2.1.3, RFC 4291 §2.5.3).</summary>
    public static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
    }

    private static Result<ServerEndpoint> Fail(string message) => Result<ServerEndpoint>.Fail(new AuthError(message, NeedsSignIn: false));
}
