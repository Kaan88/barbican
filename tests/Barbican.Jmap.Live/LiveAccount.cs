// The live suite's connection (docs/milestones/M1-plan.md §1 rule 3, §5 jmap): always a read-only client. The account comes from the
// environment, filled by the Verify stage or the user from the ADR 0008 password-kind import into a dev profile; an agent never types
// a password and an OAuth entry is never used here (a refresh would rotate the token and sign the Rust app out, B-OAUTH-015).
// Once the accounts stream's secret store is merged, the integrator may read the imported entry instead. Owner jmap.

using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Jmap.Live;

internal static class LiveAccount
{
    public const string ServerVariable = "BARBICAN_LIVE_SERVER";
    public const string UserVariable = "BARBICAN_LIVE_USER";
    public const string SecretVariable = "BARBICAN_LIVE_SECRET";

    /// <summary>Skips unless the live suite is on, then connects read-only.</summary>
    public static async Task<IJmapClient> ConnectAsync(CancellationToken ct)
    {
        LiveGate.Require();
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        var user = Environment.GetEnvironmentVariable(UserVariable);
        var secret = Environment.GetEnvironmentVariable(SecretVariable);
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(secret))
        {
            Assert.Skip($"live suite: set {ServerVariable}, {UserVariable} and {SecretVariable} (a password-kind entry from the dev profile import)");
        }

        var endpoint = ServerEndpoint.Parse(server) ?? throw new InvalidOperationException($"{ServerVariable} is not a server address");
        var factory = new JmapClientFactory(new JmapHttp(), TimeProvider.System);
        var options = new JmapClientOptions { ReadOnly = true };
        var client = await factory.ConnectAsync(endpoint, StaticAccessTokenSource.Basic(user, secret), options, ct);
        Assert.True(client.IsOk, client.Error?.Message);
        Assert.True(client.Value!.IsReadOnly);
        return client.Value;
    }
}
