using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginBindingContractTests
{
    private static PluginPackageIdentity Package(char hash = 'a') => new("probe", "1.0.0", new string(hash, 64));

    [Fact]
    public void SameIdAndVersionWithDifferentBytesCannotReuseConsent()
    {
        var original = Package();
        var grant = new PluginGrantSnapshot(original, 7, true, ["email:read"], ["account-a"]);
        Assert.True(grant.Authorizes(original, 7, "email:read", "account-a"));
        Assert.False(grant.Authorizes(Package('b'), 7, "email:read", "account-a"));
        Assert.False(grant.Authorizes(original, 8, "email:read", "account-a"));
        Assert.False(grant.Authorizes(original, 7, "email:read", "account-b"));
        Assert.False(grant.Authorizes(original, 7, "email:write", "account-a"));
        Assert.False(grant.Authorizes(original, 7, "EMAIL:READ", "account-a"));
    }

    [Fact]
    public void CallerCollectionsCannotWidenOrRevokeAnIssuedSnapshot()
    {
        var permissions = new List<string> { "email:read" };
        var accounts = new List<string> { "account-a" };
        var package = Package();
        var grant = new PluginGrantSnapshot(package, 1, true, permissions, accounts);
        permissions.Add("email:write");
        accounts.Add("account-b");
        permissions.Remove("email:read");
        Assert.True(grant.Authorizes(package, 1, "email:read", "account-a"));
        Assert.False(grant.Authorizes(package, 1, "email:write", "account-a"));
        Assert.False(grant.Authorizes(package, 1, "email:read", "account-b"));
    }

    [Fact]
    public void DisabledAndHostOnlyGrantsDoNotAuthorizeAccountCalls()
    {
        var package = Package();
        var disabled = new PluginGrantSnapshot(package, 1, false, ["email:read"], ["account-a"]);
        Assert.False(disabled.Authorizes(package, 1, "email:read", "account-a"));
        var hostOnly = new PluginGrantSnapshot(package, 2, true, ["storage:read"], []);
        Assert.True(hostOnly.Authorizes(package, 2, "storage:read", null));
        Assert.False(hostOnly.Authorizes(package, 2, "storage:read", "account-a"));
    }

    [Fact]
    public void HashCaseIsCanonicalButInvalidAndMissingHashesAreRejected()
    {
        Assert.Equal(Package(), new PluginPackageIdentity("probe", "1.0.0", new string('A', 64)));
        Assert.Throws<ArgumentException>(() => new PluginPackageIdentity("probe", "1.0.0", ""));
        Assert.Throws<ArgumentException>(() => new PluginPackageIdentity("probe", "1.0.0", new string('z', 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PluginGrantSnapshot(Package(), 0, true, [], []));
    }
}
