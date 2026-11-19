// Source: lib/plugin-hooks.ts:178-403. The production HookBus is the entrypoint under test.
using Barbican.Core.Plugins.Hooks;
using Barbican.Plugins;
namespace Barbican.Core.Tests.Plugins.Api;

public sealed class HookMediationAcceptanceTests
{
    [Fact]
    public async Task TransformsRunByOrderAndPreserveTies()
    {
        var bus = new HookBus();
        using var late = bus.Transform<int>("onEmailsFetched", (x, _) => ValueTask.FromResult((x * 10) + 3), 200);
        using var first = bus.Transform<int>("onEmailsFetched", (x, _) => ValueTask.FromResult((x * 10) + 1), 10);
        using var second = bus.Transform<int>("onEmailsFetched", (x, _) => ValueTask.FromResult((x * 10) + 2), 10);
        Assert.Equal(123, await bus.TransformAsync("onEmailsFetched", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TakeoverStopsAtHandledAndPreservesOrder()
    {
        var bus = new HookBus(); var calls = new List<int>();
        using var one = bus.TakeOver<int>("onComposeSend", (_, _) => { calls.Add(1); return ValueTask.FromResult(PluginTakeOverDecision.Unhandled); }, 10);
        using var two = bus.TakeOver<int>("onComposeSend", (_, _) => { calls.Add(2); return ValueTask.FromResult(PluginTakeOverDecision.Handled); }, 20);
        using var three = bus.TakeOver<int>("onComposeSend", (_, _) => { calls.Add(3); return ValueTask.FromResult(PluginTakeOverDecision.Unhandled); }, 30);
        Assert.Equal(HookTakeOverOutcome.Handled, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
        Assert.Collection(calls, x => Assert.Equal(1, x), x => Assert.Equal(2, x));
    }

    [Fact]
    public async Task TakeoverExceptionFailsClosed()
    {
        var bus = new HookBus();
        using var registration = bus.TakeOver<int>("onComposeSend", (_, _) => throw new InvalidOperationException("private backend text"));
        Assert.Equal(HookTakeOverOutcome.Failed, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TakeoverAbortIsDistinctFromHandledAndDefault()
    {
        var bus = new HookBus();
        using var registration = bus.TakeOver<int>("onComposeSend", (_, _) => ValueTask.FromResult(PluginTakeOverDecision.Aborted));
        Assert.Equal(HookTakeOverOutcome.Aborted, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task NoTakeoverHandlerReturnsDefault()
    {
        var bus = new HookBus();
        Assert.Equal(HookTakeOverOutcome.Default, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposedTakeoverIsNotInvoked()
    {
        var bus = new HookBus(); var invoked = false;
        var registration = bus.TakeOver<int>("onComposeSend", (_, _) => { invoked = true; return ValueTask.FromResult(PluginTakeOverDecision.Handled); });
        registration.Dispose();
        Assert.Equal(HookTakeOverOutcome.Default, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
        Assert.False(invoked);
    }

    [Fact]
    public async Task TakeoverTimeoutFailsClosedWithinHookBudget()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var bus = new HookBus(time, TimeSpan.FromSeconds(2));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<PluginTakeOverDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = bus.TakeOver<int>("onComposeSend", (_, _) => { started.SetResult(); return new ValueTask<PluginTakeOverDecision>(release.Task); });
        var result = bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(HookTakeOverOutcome.Failed, await result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        release.SetResult(PluginTakeOverDecision.Handled);
    }
    [Fact]
    public async Task ValueInterceptUsesOneOrderedPassAndVetoStopsIt()
    {
        var bus = new HookBus(); var calls = new List<int>();
        using var first = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => { calls.Add(value); return ValueTask.FromResult(HookValueDecision<int>.Replace(value + 1)); }, 10);
        using var second = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => { calls.Add(value); return ValueTask.FromResult(HookValueDecision<int>.Veto()); }, 20);
        using var third = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => { calls.Add(value); return ValueTask.FromResult(HookValueDecision<int>.Unchanged()); }, 30);
        var result = await bus.InterceptWithValueAsync("onBeforeOpenLink", 0, TestContext.Current.CancellationToken);
        Assert.False(result.Allowed); Assert.Collection(calls, x => Assert.Equal(0, x), x => Assert.Equal(1, x));
    }

    [Fact]
    public async Task ValueInterceptExceptionFailsOpenAndLaterSeesLastAcceptedValue()
    {
        var bus = new HookBus();
        using var first = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => ValueTask.FromResult(HookValueDecision<int>.Replace(value + 1)), 10);
        using var second = bus.InterceptWithValue<int>("onBeforeOpenLink", (_, _) => throw new InvalidOperationException("private"), 20);
        using var third = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => ValueTask.FromResult(HookValueDecision<int>.Replace(value * 2)), 30);
        var result = await bus.InterceptWithValueAsync("onBeforeOpenLink", 3, TestContext.Current.CancellationToken);
        Assert.True(result.Allowed); Assert.Equal(8, result.Value);
    }
    [Fact]
    public async Task LegacyTransformSharesDefaultOrderWithStableTies()
    {
        var bus = new HookBus();
        using var first = bus.Transform<int>("hook", (x, _) => ValueTask.FromResult((x * 10) + 1), 100);
        using var legacy = bus.Transform<int>("hook", (x, _) => ValueTask.FromResult((x * 10) + 2));
        using var last = bus.Transform<int>("hook", (x, _) => ValueTask.FromResult((x * 10) + 3), 100);
        Assert.Equal(123, await bus.TransformAsync("hook", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void OrderedRegistrationsRejectNullHandlers()
    {
        var bus = new HookBus();
        Assert.Throws<ArgumentNullException>(() => bus.Transform<int>("hook", null!, 100));
        Assert.Throws<ArgumentNullException>(() => bus.TakeOver<int>("hook", null!));
        Assert.Throws<ArgumentNullException>(() => bus.InterceptWithValue<int>("hook", null!));
    }
    [Fact]
    public async Task TakeoverDefaultBudgetDoesNotUseObserverFiveSecondTimeout()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(); var bus = new HookBus(time);
        var release = new TaskCompletionSource<PluginTakeOverDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = bus.TakeOver<int>("unknown-takeover", (_, _) => new ValueTask<PluginTakeOverDecision>(release.Task));
        var pending = bus.TakeOverAsync("unknown-takeover", 0, TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(100, TestContext.Current.CancellationToken)));
        release.SetResult(PluginTakeOverDecision.Handled);
        Assert.Equal(HookTakeOverOutcome.Handled, await pending);
    }

    [Fact]
    public async Task ValueInterceptDefaultBudgetDoesNotUseObserverFiveSecondTimeout()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(); var bus = new HookBus(time);
        var release = new TaskCompletionSource<HookValueDecision<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = bus.InterceptWithValue<int>("unknown-intercept", (_, _) => new ValueTask<HookValueDecision<int>>(release.Task));
        var pending = bus.InterceptWithValueAsync("unknown-intercept", 0, TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.NotSame(pending, await Task.WhenAny(pending, Task.Delay(100, TestContext.Current.CancellationToken)));
        release.SetResult(HookValueDecision<int>.Replace(7));
        var result = await pending; Assert.True(result.Allowed); Assert.Equal(7, result.Value);
    }
    [Fact]
    public async Task TakeoverTimeoutDoesNotWaitForBlockedCancellationCallback()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(); var bus = new HookBus(time, TimeSpan.FromSeconds(2));
        using var gate = new ManualResetEventSlim();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new TaskCompletionSource<PluginTakeOverDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = bus.TakeOver<int>("onComposeSend", (_, ct) =>
        {
            ct.Register(() => { callbackStarted.SetResult(); gate.Wait(); callbackFinished.SetResult(); });
            return new ValueTask<PluginTakeOverDecision>(work.Task);
        });
        var pending = bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken).AsTask();
        try
        {
            time.Advance(TimeSpan.FromSeconds(2));
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(HookTakeOverOutcome.Failed, await pending.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        finally
        {
            gate.Set(); work.TrySetResult(PluginTakeOverDecision.Unhandled);
            await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }
    [Fact]
    public async Task ValueInterceptTimeoutFailsOpenWithPreviousValue()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(); var bus = new HookBus(time, TimeSpan.FromSeconds(2));
        var work = new TaskCompletionSource<HookValueDecision<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = bus.InterceptWithValue<int>("hook", (value, _) => ValueTask.FromResult(HookValueDecision<int>.Replace(value + 1)), 10);
        using var second = bus.InterceptWithValue<int>("hook", (_, _) => new ValueTask<HookValueDecision<int>>(work.Task), 20);
        var pending = bus.InterceptWithValueAsync("hook", 3, TestContext.Current.CancellationToken).AsTask();
        time.Advance(TimeSpan.FromSeconds(2));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(result.Allowed); Assert.Equal(4, result.Value);
        work.SetResult(HookValueDecision<int>.Replace(99));
    }
}
