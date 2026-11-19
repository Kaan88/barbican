// Source: lib/plugin-sandbox/host-api-file-viewer.ts:22-24; host-api-print.ts:22-27; host-api-input.ts:183-196.
using Barbican.Core.Plugins.Api.Preview;
using Microsoft.Extensions.Time.Testing;
namespace Barbican.Core.Tests.Plugins.Api;
public sealed class PreviewRateAcceptanceTests
{
    [Fact]
    [Red("m6b-mediation")]
    public void OpenAndPrintHaveDistinctBudgetsAcrossFramesAndPackageUpdates()
    {
        var rate = PluginPresentationRateLimiter.Create(new FakeTimeProvider(), 10);
        for (var i = 0; i < 5; i++) Assert.True(rate.TryAcquire("profile", "probe", PluginPresentationOperation.OpenFile));
        Assert.False(rate.TryAcquire("profile", "probe", PluginPresentationOperation.OpenFile));
        for (var i = 0; i < 3; i++) Assert.True(rate.TryAcquire("profile", "probe", PluginPresentationOperation.Print));
        Assert.False(rate.TryAcquire("profile", "probe", PluginPresentationOperation.Print));
        Assert.True(rate.TryAcquire("other-profile", "probe", PluginPresentationOperation.OpenFile));
    }
    [Fact]
    [Red("m6b-mediation")]
    public void RollingWindowExpiresAndKeyTableFailsClosedWhenFull()
    {
        var clock = new FakeTimeProvider(); var rate = PluginPresentationRateLimiter.Create(clock, 1);
        Assert.True(rate.TryAcquire("profile", "probe", PluginPresentationOperation.OpenFile));
        Assert.False(rate.TryAcquire("profile", "other", PluginPresentationOperation.OpenFile));
        clock.Advance(TimeSpan.FromSeconds(59)); Assert.False(rate.TryAcquire("profile", "other", PluginPresentationOperation.OpenFile));
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.True(rate.TryAcquire("profile", "other", PluginPresentationOperation.OpenFile));
    }
}
