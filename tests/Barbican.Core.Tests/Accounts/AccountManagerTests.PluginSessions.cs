using Barbican.Core.Plugins.Bridge;
namespace Barbican.Core.Tests.Accounts;

public sealed partial class AccountManagerTests
{
    [Fact]
    public async Task PluginSessionSelectionAbaIsRejectedWhileExplicitOwnerRemainsCurrent()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        Assert.True((await _manager.SwitchToAsync(a.Key, Ct)).IsOk);
        var source = new PluginSessionSource(_manager);
        var active = source.CaptureActive().Value!;
        var owner = source.CaptureOwner(a.Key).Value!;
        Assert.Same(active.Session, owner.Session);
        Assert.True(source.IsCurrent(active));
        Assert.True((await _manager.SwitchToAsync(b.Key, Ct)).IsOk);
        Assert.True((await _manager.SwitchToAsync(a.Key, Ct)).IsOk);
        Assert.False(source.IsCurrent(active));
        Assert.True(source.IsCurrent(owner));
        var fresh = source.CaptureActive().Value!;
        Assert.Same(active.Session, fresh.Session);
        Assert.True(fresh.ActiveSelectionGeneration > active.ActiveSelectionGeneration);
        Assert.True(source.IsCurrent(fresh));
    }

    [Fact]
    public async Task PluginSessionRemovedAndRecreatedLoginCannotReuseOwnerOrActiveCapture()
    {
        var a = (await SignInAAsync()).Value!;
        var source = new PluginSessionSource(_manager);
        var active = source.CaptureActive().Value!;
        var owner = source.CaptureOwner(a.Key).Value!;
        Assert.True((await _manager.SignOutAsync(a.Key, Ct)).IsOk);
        Assert.False(source.IsCurrent(active));
        Assert.False(source.IsCurrent(owner));
        Assert.False(source.CaptureActive().IsOk);
        Assert.False(source.CaptureOwner(a.Key).IsOk);
        Assert.True((await SignInAAsync()).IsOk);
        var replacement = source.CaptureActive().Value!;
        Assert.NotSame(owner.Session, replacement.Session);
        Assert.False(source.IsCurrent(owner));
        Assert.True(source.IsCurrent(replacement));
    }

    [Fact]
    public async Task PluginSessionSnapshotIsCurrentInsideSelectionNotification()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        var source = new PluginSessionSource(_manager);
        var before = source.CaptureActive().Value!;
        Assert.Equal(b.Key, before.LocalAccountKey, StringComparer.Ordinal);
        var observed = false;
        _manager.ActiveChanged += (_, _) =>
        {
            Assert.False(source.IsCurrent(before));
            var during = source.CaptureActive().Value!;
            Assert.Equal(a.Key, during.LocalAccountKey, StringComparer.Ordinal);
            Assert.True(source.IsCurrent(during));
            observed = true;
        };
        Assert.True((await _manager.SwitchToAsync(a.Key, Ct)).IsOk);
        Assert.True(observed);
    }
}
