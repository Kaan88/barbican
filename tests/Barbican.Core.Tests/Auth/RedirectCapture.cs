// A launcher for tests: before the real redirect it can deliver a forged callback (another local process guessing at the loopback), and it
// remembers where the redirect went so a test can replay it.

using Barbican.Core.Auth;
using Barbican.Core.Common;

namespace Barbican.Core.Tests.Auth;

internal sealed class RedirectCapture(RedirectFollowingLauncher inner) : IAuthorizationLauncher
{
    public bool ForgeFirst { get; init; }

    public Uri? Authorize { get; private set; }

    public Uri? Redirect { get; private set; }

    public async Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct)
    {
        Authorize = authorizeUri;
        var q = LoopbackListener.ParseQuery(authorizeUri.Query);
        Redirect = new Uri(q["redirect_uri"]);
        if (ForgeFirst)
        {
            var forged = await RedirectFollowingLauncher.DeliverAsync(new Uri(Redirect + "?code=forged&state=guess"), ct);
            Assert.True(forged.IsOk);
        }

        return await inner.OpenAsync(authorizeUri, ct);
    }
}
