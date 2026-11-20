using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;

namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class PluginHttpOriginApprovalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] Origins = ["https://api.example.org", "https://other.example.org"];

    [Fact]
    public async Task InstallRequiresExactlyTheReviewedOriginsAndPersistsThem()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        using var archive = Archive();
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        Assert.True(preview.Candidate.RequestedHttpOrigins.SetEquals(Origins));

        foreach (var mismatch in new string[][] { [], [Origins[0]], [Origins[0], Origins[1], "https://extra.example.org"] })
        {
            var rejected = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity,
                ["http:fetch"], ["login-a"], approvedHttpOrigins: mismatch);
            Assert.False((await manager.InstallAsync(rejected, Ct)).IsOk);
        }
        var accepted = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity,
            ["http:fetch"], ["login-a"], approvedHttpOrigins: Origins.Reverse());
        var installed = Need(await manager.InstallAsync(accepted, Ct));
        Assert.True(installed.Grant!.ApprovedHttpOrigins.SetEquals(Origins));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);

        using var reopened = rig.Manager();
        var grant = Need(await reopened.GetCurrentAsync(PackageTestRig.PluginId, Ct));
        Assert.NotNull(grant);
        Assert.True(grant.ApprovedHttpOrigins.SetEquals(Origins));
        Assert.True(grant.Authorizes(installed.Package.Identity, grant.Generation, "http:fetch", "login-a"));
    }

    [Fact]
    public async Task LegacyAuthenticatedIndexRetainsExistingAuthorityButHasNoApprovedOrigins()
    {
        var rig = new PackageTestRig();
        using var archive = Archive();
        var bytes = archive.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var identity = new PluginPackageIdentity(PackageTestRig.PluginId, "1.0.0", hash);
        Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, identity, bytes, Ct)).IsOk);
        // The synthetic store represents an authenticated legacy index, with the new field absent.
        string[] permissions = ["http:fetch"];
        string[] accounts = ["login-a"];
        var index = JsonSerializer.SerializeToUtf8Bytes(new
        {
            NextGeneration = 2L,
            Entries = new[]
            {
                new { Id = identity.Id, Version = identity.Version, Hash = hash,
                    Enabled = true, Generation = 1L, Permissions = permissions, Accounts = accounts },
            },
        });
        Assert.True((await rig.Persistence.CommitAsync(PackageTestRig.Profile, 0, index, Ct)).IsOk);

        using var reopened = rig.Manager();
        var grant = Need(await reopened.GetCurrentAsync(identity.Id, Ct));
        Assert.NotNull(grant);
        Assert.True(grant.Authorizes(identity, 1, "http:fetch", "login-a"));
        Assert.Empty(grant.ApprovedHttpOrigins);
        var review = Need(await reopened.ReviewAsync(identity, Ct));
        var upgraded = Need(await reopened.InstallAsync(new(review.ReviewId, identity, ["http:fetch"], ["login-a"],
            approvedHttpOrigins: review.Candidate.RequestedHttpOrigins), Ct));
        Assert.False(upgraded.Enabled);
        Assert.Equal(identity, upgraded.Package.Identity);
        Assert.True(upgraded.Grant!.ApprovedHttpOrigins.SetEquals(Origins));
    }

    private static MemoryStream Archive()
    {
        const string manifest = """{"id":"fixture-plugin","name":"Fixture","version":"1.0.0","author":"Example","type":"hook","permissions":["http:fetch"],"httpOrigins":["https://api.example.org","https://other.example.org","https://invalid.example.org\n","https://invalid.example.org:99999"]}""";
        return PackageTestRig.Zip(("manifest.json", Encoding.UTF8.GetBytes(manifest)), ("index.js", Encoding.UTF8.GetBytes("export default 1")));
    }

    private static T Need<T>(Result<T> result) { Assert.True(result.IsOk, result.Error?.Message); return result.Value!; }
}
