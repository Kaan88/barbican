// Source: lib/plugin-hooks.ts:178-403. The production HookBus is the entrypoint under test.
using Barbican.Core.Plugins.Hooks;
using Barbican.Plugins;
namespace Barbican.Core.Tests.Plugins.Api;

public sealed class HookMediationAcceptanceTests
{
    [Fact]
    [Red("m6b-mediation")]
    public async Task TransformsRunByOrderAndPreserveTies()
    {
        var bus = new HookBus();
        using var late = bus.Transform<int>("onEmailsFetched", (x, _) => ValueTask.FromResult((x * 10) + 3), 200);
        using var first = bus.Transform<int>("onEmailsFetched", (x, _) => ValueTask.FromResult((x * 10) + 1), 10);
        using var second = bus.Transform<int>("onEmailsFetched", (x, _) => ValueTask.FromResult((x * 10) + 2), 10);
        Assert.Equal(123, await bus.TransformAsync("onEmailsFetched", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Red("m6b-mediation")]
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
    [Red("m6b-mediation")]
    public async Task TakeoverExceptionFailsClosed()
    {
        var bus = new HookBus();
        using var registration = bus.TakeOver<int>("onComposeSend", (_, _) => throw new InvalidOperationException("private backend text"));
        Assert.Equal(HookTakeOverOutcome.Failed, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Red("m6b-mediation")]
    public async Task TakeoverAbortIsDistinctFromHandledAndDefault()
    {
        var bus = new HookBus();
        using var registration = bus.TakeOver<int>("onComposeSend", (_, _) => ValueTask.FromResult(PluginTakeOverDecision.Aborted));
        Assert.Equal(HookTakeOverOutcome.Aborted, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task NoTakeoverHandlerReturnsDefault()
    {
        var bus = new HookBus();
        Assert.Equal(HookTakeOverOutcome.Default, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Red("m6b-mediation")]
    public async Task DisposedTakeoverIsNotInvoked()
    {
        var bus = new HookBus(); var invoked = false;
        var registration = bus.TakeOver<int>("onComposeSend", (_, _) => { invoked = true; return ValueTask.FromResult(PluginTakeOverDecision.Handled); });
        registration.Dispose();
        Assert.Equal(HookTakeOverOutcome.Default, await bus.TakeOverAsync("onComposeSend", 0, TestContext.Current.CancellationToken));
        Assert.False(invoked);
    }

    [Fact]
    [Red("m6b-mediation")]
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
    [Red("m6b-mediation")]
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
    [Red("m6b-mediation")]
    public async Task ValueInterceptExceptionFailsOpenAndLaterSeesLastAcceptedValue()
    {
        var bus = new HookBus();
        using var first = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => ValueTask.FromResult(HookValueDecision<int>.Replace(value + 1)), 10);
        using var second = bus.InterceptWithValue<int>("onBeforeOpenLink", (_, _) => throw new InvalidOperationException("private"), 20);
        using var third = bus.InterceptWithValue<int>("onBeforeOpenLink", (value, _) => ValueTask.FromResult(HookValueDecision<int>.Replace(value * 2)), 30);
        var result = await bus.InterceptWithValueAsync("onBeforeOpenLink", 3, TestContext.Current.CancellationToken);
        Assert.True(result.Allowed); Assert.Equal(8, result.Value);
    }
}
