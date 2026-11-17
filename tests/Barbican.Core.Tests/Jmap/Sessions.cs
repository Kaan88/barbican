// Test shorthand: a session fetch or parse where the server the user typed is the origin of the URL itself (the case of every transport test
// that does not exercise the session URL rewrite of Fable review 1 F-02). Owner jmap.

using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Tests.Jmap;

internal static class Sessions
{
    public static ServerEndpoint ServerOf(Uri url) => new(new Uri(url.GetLeftPart(UriPartial.Authority)));

    public static Task<Result<JmapSession>> FetchAsync(IJmapHttp http, IAccessTokenSource auth, Uri url, TimeSpan timeout, TimeProvider time, CancellationToken ct) =>
        SessionFetcher.FetchAsync(http, auth, ServerOf(url), url, timeout, time, ct);

    public static Result<JmapSession> Parse(string json, Uri sessionUri) => SessionParser.Parse(json, sessionUri, ServerOf(sessionUri), viaOtherOrigin: false);
}
