// Mirrors: Bulwark lib/jmap/client.ts:2326 rewriteSessionUrl (a URL keeps its path and query and takes the origin the user typed), :2347
// rewriteSessionUrls (apiUrl always moves; downloadUrl, uploadUrl and eventSourceUrl stay only when they name another https origin than the
// server's own reported apiUrl, a host it deliberately serves them from), :152 absoluteOrigin; RFC 8620 §2 (session URLs may be relative,
// resolved against the session resource). Fable review 1 F-02. Owner jmap.

namespace Barbican.Core.Jmap.Transport;

/// <summary>Moves the URLs of one session document onto the origin the user typed, and collects the origins credentials may go to.</summary>
internal sealed class SessionUrls
{
    private readonly Uri _sessionUri;
    private readonly string _typed;
    private readonly string? _reported;
    private readonly bool _viaOtherOrigin;
    private readonly List<string> _origins;

    /// <param name="sessionUri">Where the session was read (relative URLs resolve against it).</param>
    /// <param name="server">The server the user typed.</param>
    /// <param name="reportedApiOrigin">The origin of the session's own absolute apiUrl, or null when it is relative.</param>
    /// <param name="viaOtherOrigin">The session came from another origin than the typed one: nothing stays off the typed origin.</param>
    public SessionUrls(Uri sessionUri, ServerEndpoint server, string? reportedApiOrigin, bool viaOtherOrigin)
    {
        _sessionUri = sessionUri;
        _typed = server.BaseUri.GetLeftPart(UriPartial.Authority);
        _reported = reportedApiOrigin;
        _viaOtherOrigin = viaOtherOrigin;
        _origins = [_typed];
    }

    /// <summary>The origins the Authorization header may be sent to: the typed one, then each https host kept by <see cref="Rewrite"/>.</summary>
    public IReadOnlyList<string> Origins => _origins;

    /// <summary>The URL on the typed origin, with its own path and query (template braces kept as written).</summary>
    public string Move(string url)
    {
        var absolute = SessionParser.Absolute(_sessionUri, url);
        var scheme = absolute.IndexOf("//", StringComparison.Ordinal);
        var path = absolute.IndexOf('/', scheme + 2);
        return _typed + (path < 0 ? "" : absolute[path..]);
    }

    /// <summary>
    /// <see cref="Move"/>, except for a URL on another https origin than the reported apiUrl (a deliberate download or push host, as
    /// Fastmail's), which stays as it is and joins <see cref="Origins"/>.
    /// </summary>
    public string Rewrite(string url)
    {
        var origin = SessionParser.AbsoluteOrigin(url);
        var elsewhere = !_viaOtherOrigin
            && origin is not null && _reported is not null
            && !string.Equals(origin, _reported, StringComparison.OrdinalIgnoreCase)
            && origin.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
        if (!elsewhere)
        {
            return Move(url);
        }

        if (!_origins.Contains(origin!, StringComparer.OrdinalIgnoreCase))
        {
            _origins.Add(origin!);
        }

        return url;
    }
}
