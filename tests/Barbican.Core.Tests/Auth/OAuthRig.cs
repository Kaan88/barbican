// One FakeJmap server with the FakeOAuthProvider in front, the app's OAuth pieces wired over it, and a throwaway profile.

using Barbican.Core.Auth;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Auth;

internal sealed class OAuthRig : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-oauth-" + Guid.NewGuid().ToString("N"));

    public OAuthRig(bool registration = true, TimeProvider? time = null)
    {
        Server = new FakeJmapServer();
        Provider = new FakeOAuthProvider(Server, registration);
        Http = new JmapHttp(() => Provider);
        Time = time ?? TimeProvider.System;
        Paths = new TestProfilePaths(_root);
        OAuth = new OAuthHttp(Http, Time);
        Clients = new OAuthClientRegistry(OAuth, Paths);
        Launcher = new RedirectFollowingLauncher(Http);
    }

    public FakeJmapServer Server { get; }

    public FakeOAuthProvider Provider { get; }

    public JmapHttp Http { get; }

    public TimeProvider Time { get; }

    public TestProfilePaths Paths { get; }

    public OAuthHttp OAuth { get; }

    public OAuthClientRegistry Clients { get; }

    public RedirectFollowingLauncher Launcher { get; }

    public Uri Base => new(Server.Base);

    public Uri TokenEndpoint => new(Server.Base + "/oauth/token");

    public OAuthFlow Flow(IAuthorizationLauncher? launcher = null) =>
        new(new OAuthDiscovery(OAuth), Clients, OAuth, launcher ?? Launcher, new OAuthFlowOptions { LoopbackPort = 0, Timeout = TimeSpan.FromSeconds(10) }, Time);

    public void Dispose()
    {
        Clients.Dispose();
        Http.Dispose();
        Provider.Dispose();
        Server.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
