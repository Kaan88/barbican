using System.Text.Json;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Presentation.Plugins;

namespace Barbican.Presentation.Tests.Plugins;

public sealed class AddonPresentationTests
{
    [Fact]
    public void Installation_displays_exact_identity_and_grants_without_inferring_activation()
    {
        var package = Package("1.0", 'a', ["mail:read", "ui:observe"]);
        var grant = new PluginGrantSnapshot(package.Identity, 1, false, ["mail:read"], [], PluginExecutionTier.Untrusted);
        var view = AddonInstallationView.From(new PluginInstallation(package, true, grant, "Example error"));

        Assert.Equal("Example", view.Name);
        Assert.Equal(package.Identity, view.Identity);
        Assert.True(view.Enabled);
        Assert.False(view.GrantEnabled);
        Assert.Equal("Example error", view.Error);
        Assert.Equal(["mail:read", "ui:observe"], view.RequestedPermissions);
        Assert.Equal(["mail:read"], view.GrantedPermissions);
    }

    [Fact]
    public void Update_diff_compares_requested_permissions_not_prior_grants()
    {
        var old = Package("1.0", 'a', ["mail:read", "ui:observe"]);
        var grant = new PluginGrantSnapshot(old.Identity, 1, true, ["mail:read"], []);
        var candidate = Package("1.0", 'b', ["ui:observe", "mail:write"]);
        var preview = new PluginInstallPreview("review-1", candidate, new PluginInstallation(old, true, grant));
        var view = AddonReviewView.From(preview);

        Assert.Equal("review-1", view.ReviewId);
        Assert.Equal(old.Identity, view.PreviousIdentity);
        Assert.Equal(candidate.Identity, view.CandidateIdentity);
        Assert.True(view.ContentChanged);
        Assert.Equal(["mail:write"], view.AddedPermissions);
        Assert.Equal(["mail:read"], view.RemovedPermissions);
        Assert.Equal(["ui:observe"], view.UnchangedPermissions);
        Assert.Equal(["mail:read"], view.PreviouslyGrantedPermissions);
    }

    [Fact]
    public void Fresh_install_has_no_previous_identity_and_all_requests_are_added()
    {
        var candidate = Package("1.0", 'a', ["z", "A", "a"]);
        var view = AddonReviewView.From(new PluginInstallPreview("new", candidate, null));
        Assert.Null(view.PreviousIdentity);
        Assert.False(view.ContentChanged);
        Assert.Equal(["A", "a", "z"], view.AddedPermissions);
        Assert.Empty(view.RemovedPermissions);
        Assert.Empty(view.UnchangedPermissions);
        Assert.Empty(view.PreviouslyGrantedPermissions);
    }

    [Fact]
    public void Mismatched_prior_identity_is_rejected_instead_of_showing_an_unrelated_diff()
    {
        var old = Package("1.0", 'a', []);
        var other = Package("2.0", 'b', [], "other");
        Assert.Throws<ArgumentException>(() => AddonReviewView.From(
            new PluginInstallPreview("review", other, new PluginInstallation(old, false, null))));
    }

    [Fact]
    public void Mismatched_grant_is_not_presented_as_the_installations_grant()
    {
        var package = Package("1.0", 'a', []);
        var other = Package("1.0", 'b', []);
        var grant = new PluginGrantSnapshot(other.Identity, 1, true, ["mail:read"], []);
        Assert.Throws<ArgumentException>(() => AddonInstallationView.From(new PluginInstallation(package, true, grant)));
        Assert.Throws<ArgumentException>(() => AddonReviewView.From(
            new PluginInstallPreview("review", package, new PluginInstallation(package, true, grant))));
    }

    [Fact]
    public void Native_and_requested_tier_are_exposed_without_claiming_trust()
    {
        using var manifest = JsonDocument.Parse("{}");
        var package = new PluginPackageDefinition(new PluginPackageIdentity("native", "2", new string('c', 64)),
            PluginRuntimeKind.Native, "plugin.dll", manifest.RootElement, [], PluginPackageSource.LocalImport,
            PluginExecutionTier.NativeFullTrust);
        var installation = AddonInstallationView.From(new PluginInstallation(package, false, null));
        var review = AddonReviewView.From(new PluginInstallPreview("review", package, new PluginInstallation(package, false, null)));

        Assert.Equal("native", installation.Name);
        Assert.Equal(PluginRuntimeKind.Native, review.CandidateRuntimeKind);
        Assert.Equal(PluginExecutionTier.NativeFullTrust, review.CandidateRequestedTier);
        Assert.Null(installation.GrantEnabled);
        Assert.Empty(installation.GrantedPermissions);
        Assert.False(review.ContentChanged);
    }

    private static PluginPackageDefinition Package(string version, char hash, string[] permissions, string id = "example")
    {
        using var manifest = JsonDocument.Parse("""{"name":"Example"}""");
        return new PluginPackageDefinition(new PluginPackageIdentity(id, version, new string(hash, 64)),
            PluginRuntimeKind.JavaScript, "index.js", manifest.RootElement, permissions, PluginPackageSource.LocalImport);
    }
}
