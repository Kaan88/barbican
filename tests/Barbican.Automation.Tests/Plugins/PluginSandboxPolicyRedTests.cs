// Source-derived policy tests: Bulwark proxy.ts:365-480, host-bridge.ts:356-370; M6b security invariants.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Automation.Tests.Plugins;
public sealed class PluginSandboxPolicyRedTests
{
    [Fact]
    public void Untrusted_frame_retains_form_events_but_cannot_submit_or_gain_same_origin()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.Contains("allow-scripts", policy.SandboxFlags, StringComparison.Ordinal);
        Assert.Contains("allow-forms", policy.SandboxFlags, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", policy.SandboxFlags, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }
    [Fact]
    public void Verified_privileged_tier_has_same_origin_while_untrusted_tier_does_not()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.Contains("allow-same-origin", policy.FlagsFor(PluginExecutionTier.Privileged), StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", policy.FlagsFor(PluginExecutionTier.Untrusted), StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }
    [Fact]
    public void Native_full_trust_is_not_a_javascript_sandbox_tier()
    {
        var policy = new PluginSandboxResourcePolicy();
        try { _ = policy.FlagsFor(PluginExecutionTier.NativeFullTrust); }
        catch (ArgumentOutOfRangeException) { return; }
        Assert.Fail("Native full trust must not select a JavaScript sandbox.");
    }
    [Fact]
    public void Arbitrary_network_filesystem_and_route_escape_are_denied()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.False(policy.Allows(new Uri("https://example.org/path"), "fetch"));
        Assert.False(policy.Allows(new Uri("https://127.0.0.1/private"), "fetch"));
        Assert.False(policy.Allows(new Uri("file:///private.txt"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/mail/credentials"), "fetch"));
        Assert.False(policy.Allows(new Uri("https://example.org/redirect"), "navigation"));
    }    [Fact]
    public void Only_exact_host_documents_and_verified_bootstrap_assets_are_allowed()
    {
        var policy = new PluginSandboxResourcePolicy(["/assets/runtime-a1.js", "/assets/kit-a1.css"]);
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/host/index.html"), "top-navigation"));
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/sandbox/index.html?frame=slot"), "frame-navigation"));
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/assets/runtime-a1.js"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/runtime-a1.js"), "fetch"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/not-reviewed.js"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/sandbox/index.html"), "top-navigation"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/host/index.html?extra=1"), "top-navigation"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/%72untime-a1.js"), "script"));
    }

}
