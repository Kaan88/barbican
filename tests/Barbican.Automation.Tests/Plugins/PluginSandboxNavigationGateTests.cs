using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxNavigationGateTests
{
    private const string Host = "https://plugin.invalid/host/index.html";
    private const string Background = "https://plugin.invalid/sandbox/index.html";
    private const string Slot = Background + "?frame=slot";
    private const string Managed = "https://managed-plugin.invalid/sandbox/index.html";

    [Theory]
    [InlineData(PluginFrameKind.Background, Managed, Background)]
    [InlineData(PluginFrameKind.Slot, Managed + "?frame=slot", Managed)]
    public void Privileged_navigation_accepts_only_its_exact_cross_origin_child(PluginFrameKind kind, string allowed, string other)
    {
        var policy = new PluginSandboxResourcePolicy(PluginExecutionTier.Privileged);
        var gate = new PluginSandboxNavigationGate(policy, kind);
        Assert.True(gate.TryStart(Host, topLevel: true));
        Assert.True(gate.TryStart(allowed, topLevel: false));
        Assert.False(gate.TryStart(Host, topLevel: false));

        var refused = new PluginSandboxNavigationGate(policy, kind);
        Assert.True(refused.TryStart(Host, topLevel: true));
        Assert.False(refused.TryStart(other, topLevel: false));
        Assert.False(refused.TryStart(allowed, topLevel: false));
    }

    [Theory]
    [InlineData(PluginFrameKind.Background, Background)]
    [InlineData(PluginFrameKind.Slot, Slot)]
    public void Allows_only_one_parent_and_one_selected_child(PluginFrameKind kind, string child)
    {
        var gate = new PluginSandboxNavigationGate(new(), kind);

        Assert.True(gate.TryStart(Host, topLevel: true));
        Assert.True(gate.TryStart(child, topLevel: false));
        Assert.False(gate.TryStart(child, topLevel: false));
        Assert.False(gate.TryStart(Host, topLevel: true));
    }

    [Theory]
    [InlineData(Slot, false)]
    [InlineData(Host, false)]
    [InlineData(Host, true)]
    [InlineData(Background, true)]
    [InlineData("https://example.org/", false)]
    public void An_unexpected_navigation_permanently_closes_the_gate(string url, bool topLevel)
    {
        var gate = new PluginSandboxNavigationGate(new(), PluginFrameKind.Background);

        Assert.True(gate.TryStart(Host, topLevel: true));
        Assert.False(gate.TryStart(url, topLevel));
        Assert.False(gate.TryStart(Background, topLevel: false));
    }

    [Fact]
    public void A_child_cannot_start_before_its_parent()
    {
        var gate = new PluginSandboxNavigationGate(new(), PluginFrameKind.Background);

        Assert.False(gate.TryStart(Background, topLevel: false));
        Assert.False(gate.TryStart(Host, topLevel: true));
    }
}
