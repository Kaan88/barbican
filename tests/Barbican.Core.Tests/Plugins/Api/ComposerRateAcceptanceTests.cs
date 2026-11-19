// Source: lib/plugin-sandbox/host-api-composer.ts:36-42 and host-api-input.ts:183-196.
using Barbican.Core.Plugins.Api.Composer;
using Microsoft.Extensions.Time.Testing;
namespace Barbican.Core.Tests.Plugins.Api;
public sealed class ComposerRateAcceptanceTests
{
    [Fact]
    [Red("m6b-mediation")]
    public void TwoFramesAndContentUpdatesShareStablePluginBudget()
    {
        var limiter = PluginComposerEditRateLimiter.Create(new FakeTimeProvider(), 10);
        for (var i = 0; i < 60; i++) Assert.True(limiter.TryAcquire("profile", "probe"));
        // A second frame and a replacement package still use the same stable ID.
        Assert.False(limiter.TryAcquire("profile", "probe"));
        Assert.True(limiter.TryAcquire("profile", "other"));
        Assert.True(limiter.TryAcquire("other-profile", "probe"));
    }
    [Fact]
    [Red("m6b-mediation")]
    public void LimitExpiresOnlyAfterRollingWindow()
    {
        var time = new FakeTimeProvider(); var limiter = PluginComposerEditRateLimiter.Create(time, 10);
        for (var i = 0; i < 60; i++) Assert.True(limiter.TryAcquire("profile", "probe"));
        time.Advance(TimeSpan.FromSeconds(59)); Assert.False(limiter.TryAcquire("profile", "probe"));
        time.Advance(TimeSpan.FromSeconds(1)); Assert.True(limiter.TryAcquire("profile", "probe"));
    }
    [Fact]
    [Red("m6b-mediation")]
    public void FullKeyTableRefusesNewKeysUntilExpired()
    {
        var time = new FakeTimeProvider(); var limiter = PluginComposerEditRateLimiter.Create(time, 1);
        Assert.True(limiter.TryAcquire("profile", "first"));
        Assert.False(limiter.TryAcquire("profile", "second"));
        time.Advance(TimeSpan.FromSeconds(60)); Assert.True(limiter.TryAcquire("profile", "second"));
    }
}
