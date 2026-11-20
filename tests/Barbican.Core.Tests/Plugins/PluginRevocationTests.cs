using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginRevocationTests
{
    private static PluginPackageIdentity Package(char hash = 'a') => new("probe", "1.0.0", new string(hash, 64));
    private static PluginRuntimeBinding Binding(long generation = 1, char hash = 'a', string profile = "auto-revoke") =>
        PluginRuntimeBinding.Create(profile, new PluginGrantSnapshot(Package(hash), generation, true, [], []), PluginFrameKind.Background);

    [Fact]
    public async Task RevocationClosesBindingBeforeCallingTeardownAndRejectsLateOldGeneration()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var binding = Binding();
        var count = 0;
        Assert.True(coordinator.Track(binding, _ => { Assert.False(binding.IsAlive); Interlocked.Increment(ref count); return ValueTask.CompletedTask; }).IsOk);
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(1, count);
        Assert.True(binding.Lifetime.IsCancellationRequested);
        await using var late = Binding();
        Assert.False(coordinator.Track(late, _ => ValueTask.CompletedTask).IsOk);
        Assert.False(late.IsAlive);
        Assert.Equal(0, coordinator.TrackedCount);
    }

    [Fact]
    public async Task BindingDisposalIsOrderedWithTheSynchronousNativePost()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var binding = Binding();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var write = Task.Run(() => binding.TryWriteWhileAlive(() =>
        {
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(binding.IsAlive);
        }), ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var close = Task.Run(async () =>
        {
            closing.SetResult();
            await binding.DisposeAsync();
        }, ct);
        try
        {
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(close.IsCompleted);
        }
        finally { release.Set(); }

        Assert.True(await write);
        await close;
        Assert.False(binding.TryWriteWhileAlive(() => throw new InvalidOperationException("Closed binding posted.")));
    }

    [Fact]
    public async Task AllAffectedBindingsCloseBeforeAnyTeardownRuns()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var a = Binding();
        await using var b = Binding();
        using var first = coordinator.Track(a, _ => { Assert.False(b.IsAlive); return ValueTask.CompletedTask; }).Value!;
        using var second = coordinator.Track(b, _ => { Assert.False(a.IsAlive); return ValueTask.CompletedTask; }).Value!;
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
    }

    [Fact]
    public async Task Profile_close_rejects_new_bindings_and_shares_inflight_teardown()
    {
        var ct = TestContext.Current.CancellationToken;
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var first = Binding();
        await using var second = Binding(2, 'b');
        await using var late = Binding(3, 'c');
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var a = coordinator.Track(first, async _ =>
        {
            Assert.False(second.IsAlive);
            Interlocked.Increment(ref calls);
            await release.Task.WaitAsync(ct);
        }).Value!;
        using var b = coordinator.Track(second, _ =>
        {
            Assert.False(first.IsAlive);
            Interlocked.Increment(ref calls);
            return ValueTask.CompletedTask;
        }).Value!;

        var closing = coordinator.CloseAsync(ct);
        Assert.False(first.IsAlive);
        Assert.False(second.IsAlive);
        Assert.False(coordinator.Track(late, _ => ValueTask.CompletedTask).IsOk);
        Assert.False(late.IsAlive);
        var repeated = coordinator.CloseAsync(ct);
        release.SetResult();

        Assert.All(await Task.WhenAll(closing, repeated), result => Assert.True(result.IsOk));
        Assert.Equal(2, calls);
        Assert.Equal(0, coordinator.TrackedCount);
    }

    [Fact]
    public async Task RevocationDoesNotCloseNewerContentOrGeneration()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var old = Binding();
        await using var newer = Binding(2);
        await using var different = Binding(1, 'b');
        using var a = coordinator.Track(old, _ => ValueTask.CompletedTask).Value!;
        using var b = coordinator.Track(newer, _ => ValueTask.CompletedTask).Value!;
        using var c = coordinator.Track(different, _ => ValueTask.CompletedTask).Value!;
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
        Assert.False(old.IsAlive);
        Assert.True(newer.IsAlive);
        Assert.True(different.IsAlive);
        Assert.Equal(2, coordinator.TrackedCount);
    }

    [Fact]
    public async Task ConcurrentRevocationsShareTheSamePendingTeardown()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var binding = Binding();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var tracked = coordinator.Track(binding, async _ => { Interlocked.Increment(ref calls); started.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }).Value!;
        var first = coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken);
        release.SetResult();
        Assert.All(await Task.WhenAll(first, second), result => Assert.True(result.IsOk));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedTeardownDoesNotSkipOthersAndCanBeRetriedWithoutLeakingDetails()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var binding = Binding();
        var attempts = 0;
        var other = 0;
        using var a = coordinator.Track(binding, _ => Interlocked.Increment(ref attempts) == 1 ? throw new InvalidOperationException("synthetic-private-detail") : ValueTask.CompletedTask).Value!;
        using var b = coordinator.Track(binding, _ => { Interlocked.Increment(ref other); return ValueTask.CompletedTask; }).Value!;
        var failed = await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken);
        Assert.False(failed.IsOk);
        Assert.DoesNotContain("synthetic-private-detail", failed.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(1, other);
        Assert.Equal(1, coordinator.TrackedCount);
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(2, attempts);
        Assert.Equal(1, other);
        Assert.Equal(0, coordinator.TrackedCount);
    }

    [Fact]
    public async Task TimeoutRetainsPendingWorkAndRetryDoesNotInvokeItTwice()
    {
        var clock = new FakeTimeProvider();
        var coordinator = new PluginRevocationCoordinator("auto-revoke", clock, TimeSpan.FromSeconds(1));
        await using var binding = Binding();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var tracked = coordinator.Track(binding, async _ => { Interlocked.Increment(ref calls); started.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }).Value!;
        var first = coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False((await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(1, coordinator.TrackedCount);
        var retry = coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken);
        release.SetResult();
        Assert.True((await retry.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(1, calls);
        Assert.Equal(0, coordinator.TrackedCount);
    }

    [Fact]
    public async Task CancelledCallerWaitDoesNotUndoRevocationOrAbandonCleanup()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var binding = Binding();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var tracked = coordinator.Track(binding, async _ => { started.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }).Value!;
        using var cancellation = new CancellationTokenSource();
        var first = coordinator.RevokeAsync(Package(), 1, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        Assert.False((await first).IsOk);
        Assert.False(binding.IsAlive);
        release.SetResult();
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
    }

    [Fact]
    public async Task FaultingLifetimeNotificationCannotSkipExplicitCleanup()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        var binding = Binding();
        using var notification = binding.Lifetime.Register(() => throw new InvalidOperationException("synthetic-private-notification"));
        var cleaned = false;
        using var tracked = coordinator.Track(binding, _ => { cleaned = true; return ValueTask.CompletedTask; }).Value!;
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
        Assert.False(binding.IsAlive);
        Assert.True(cleaned);
        Assert.Equal(0, coordinator.TrackedCount);
    }

    [Fact]
    public async Task DetachingRegistrationAfterFailedCleanupDoesNotAbandonRetry()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke");
        await using var binding = Binding();
        var attempts = 0;
        var tracked = coordinator.Track(binding, _ => ++attempts == 1 ? throw new InvalidOperationException("first attempt") : ValueTask.CompletedTask).Value!;
        Assert.False((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
        tracked.Dispose();
        Assert.Equal(1, coordinator.TrackedCount);
        Assert.True((await coordinator.RevokeAsync(Package(), 1, TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(2, attempts);
        Assert.Equal(0, coordinator.TrackedCount);
    }

    [Fact]
    public async Task ForeignProfileAndRegistrationLimitDoNotCloseUnrelatedBindings()
    {
        var coordinator = new PluginRevocationCoordinator("auto-revoke", maxResources: 1);
        await using var foreign = Binding(profile: "auto-other");
        Assert.False(coordinator.Track(foreign, _ => ValueTask.CompletedTask).IsOk);
        Assert.True(foreign.IsAlive);
        await using var first = Binding();
        await using var second = Binding();
        using var tracked = coordinator.Track(first, _ => ValueTask.CompletedTask).Value!;
        Assert.False(coordinator.Track(second, _ => ValueTask.CompletedTask).IsOk);
        Assert.True(second.IsAlive);
    }
}
