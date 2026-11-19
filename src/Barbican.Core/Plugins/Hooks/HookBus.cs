// Mirrors: Bulwark lib/plugin-hook-catalog.ts (observer / intercept / transform), lib/plugin-mail-hooks.ts (dispatch order, a failing
// handler never stops the app), PORT-PLAN.md M6b (host-agnostic bus first, plugin hosts later); M1-plan §4.1. Owner mailcore (M1); budgets and fail-closed
// intercepts (fable-review-2 R2-10, Bulwark lib/plugin-hooks.ts:62-66, :186-188, :290-325) owner unified (M2 wave 2).

namespace Barbican.Core.Plugins.Hooks;

/// <summary>
/// The in-process hook bus. Handlers are registered by tests today and by the plugin hosts in M6b. With no handler for a hook
/// every call is a dictionary lookup and returns a completed task: no allocation, so call sites cost nothing without plugins.
/// </summary>
public sealed class HookBus : IHookBus
{
    /// <summary>How long one observer or transform handler may take before the bus gives up on it (Fable F-15): a plugin that never returns must not stop the sync.</summary>
    public static readonly TimeSpan DefaultHandlerTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly TimeSpan? _timeout;
    private readonly Func<string, HookPolicy> _policies;
    private readonly Lock _gate = new();
    // copy on write: a registration builds a new dictionary and swaps it in, so readers (the hot path) never lock and never allocate
    private volatile Dictionary<string, object[]> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a bus. Each hook's budget and fail-closed flag come from the catalogue (<see cref="HookPolicies.For"/>, R2-10): 5 s for observers and
    /// transforms, 60 s for intercepts (they wait on dialogs), longer for send and upload. <paramref name="handlerTimeout"/> replaces every budget (tests);
    /// a handler past its budget is abandoned and reported through <see cref="HandlerFailed"/>.
    /// </summary>
    public HookBus(TimeProvider? time = null, TimeSpan? handlerTimeout = null, Func<string, HookPolicy>? policies = null)
    {
        _time = time ?? TimeProvider.System;
        _timeout = handlerTimeout;
        _policies = policies ?? HookPolicies.For;
    }

    /// <summary>A handler threw; the bus carries on with the next one (a plugin must not break the app).</summary>
    public event EventHandler<HookFailedEventArgs>? HandlerFailed;

    /// <inheritdoc />
    public bool HasHandlers(string hook) => _handlers.ContainsKey(hook);

    /// <summary>Registers an observer; dispose the result to remove it.</summary>
    public IDisposable Observe<TPayload>(string hook, Func<TPayload, CancellationToken, ValueTask> handler) => Add(hook, handler);

    /// <summary>Registers an interceptor (false stops the action); dispose the result to remove it.</summary>
    public IDisposable Intercept<TPayload>(string hook, Func<TPayload, CancellationToken, ValueTask<bool>> handler) => Add(hook, handler);

    /// <summary>Registers a transform (sees the previous result, returns the next); dispose the result to remove it.</summary>
    public IDisposable Transform<T>(string hook, Func<T, CancellationToken, ValueTask<T>> handler) => Add(hook, handler);

#pragma warning disable CA1822 // Instance methods are pending registrations on this bus.
    /// <summary>Registers an ordered transform; equal order preserves registration order.</summary>
    public IDisposable Transform<T>(string hook, Func<T, CancellationToken, ValueTask<T>> handler, int order) =>
        throw new Barbican.Core.Common.PortPendingException("ordered hook transforms");

    /// <summary>Registers an ordered takeover.</summary>
    public IDisposable TakeOver<TPayload>(string hook, Func<TPayload, CancellationToken, ValueTask<Barbican.Plugins.PluginTakeOverDecision>> handler, int order = 100) =>
        throw new Barbican.Core.Common.PortPendingException("hook takeover registration");
#pragma warning restore CA1822

    /// <inheritdoc />
    public ValueTask<HookTakeOverOutcome> TakeOverAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) =>
        throw new Barbican.Core.Common.PortPendingException("hook takeover dispatch");
    /// <summary>Registers an observer that takes any payload type (the native plugin host: a plugin cannot name the app's payload types).</summary>
    public IDisposable ObserveAny(string hook, Func<object?, CancellationToken, ValueTask> handler) => Add(hook, new AnyObserver(handler));

    /// <summary>Registers an interceptor that takes any payload type (the native plugin host); false stops the action.</summary>
    public IDisposable InterceptAny(string hook, Func<object?, CancellationToken, ValueTask<bool>> handler) => Add(hook, new AnyInterceptor(handler));

    /// <inheritdoc />
    public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) =>
        _handlers.TryGetValue(hook, out var list) ? NotifyCoreAsync(hook, list, payload, ct) : default;

    private async ValueTask NotifyCoreAsync<TPayload>(string hook, object[] list, TPayload payload, CancellationToken ct)
    {
        foreach (var d in list)
        {
            Func<TPayload, CancellationToken, ValueTask>? observer = d switch
            {
                Func<TPayload, CancellationToken, ValueTask> typed => typed,
                AnyObserver any => (p, t) => any.Handler(p, t),
                _ => null,
            };
            if (observer is null)
            {
                continue;
            }

            try
            {
                await RunAsync(hook, async t =>
                {
                    await observer(payload, t).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Failed(hook, e);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) =>
        _handlers.TryGetValue(hook, out var list) ? InterceptCoreAsync(hook, list, payload, ct) : ValueTask.FromResult(true);

    private async ValueTask<bool> InterceptCoreAsync<TPayload>(string hook, object[] list, TPayload payload, CancellationToken ct)
    {
        foreach (var d in list)
        {
            Func<TPayload, CancellationToken, ValueTask<bool>>? interceptor = d switch
            {
                Func<TPayload, CancellationToken, ValueTask<bool>> typed => typed,
                AnyInterceptor any => (p, t) => any.Handler(p, t),
                _ => null,
            };
            if (interceptor is null)
            {
                continue;
            }

            try
            {
                // a handler that timed out counts as "allow", unless the hook fails closed (Bulwark failClosed: sending cannot be taken back)
                var run = await RunAsync(hook, async t => await interceptor(payload, t).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (run.Done ? !run.Value : _policies(hook).FailClosed)
                {
                    return false;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Failed(hook, e);
                if (_policies(hook).FailClosed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc />
    public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) =>
        _handlers.TryGetValue(hook, out var list) ? TransformCoreAsync(hook, list, value, ct) : ValueTask.FromResult(value);

    private async ValueTask<T> TransformCoreAsync<T>(string hook, object[] list, T value, CancellationToken ct)
    {
        foreach (var d in list)
        {
            if (d is not Func<T, CancellationToken, ValueTask<T>> transform)
            {
                continue;
            }

            try
            {
                var previous = value;
                var run = await RunAsync(hook, async t => await transform(previous, t).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (run.Done)
                {
                    value = run.Value;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Failed(hook, e);
            }
        }

        return value;
    }

    /// <summary>Runs one handler; past the timeout it is told to cancel, left behind, and reported (Done is false).</summary>
    private async Task<(bool Done, T Value)> RunAsync<T>(string hook, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var work = call(handlerCts.Token);
        if (work.IsCompleted)
        {
            // an answer that is already there stays synchronous: a key press only waits for such an answer (KeyRouter)
            return (true, await work.ConfigureAwait(false));
        }

        var budget = _timeout ?? _policies(hook).Budget;
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(budget, _time, delayCts.Token);
        var first = await Task.WhenAny(work, delay).ConfigureAwait(false);
        if (ReferenceEquals(first, work))
        {
            await delayCts.CancelAsync().ConfigureAwait(false);
            return (true, await work.ConfigureAwait(false));
        }

        ct.ThrowIfCancellationRequested();
        await handlerCts.CancelAsync().ConfigureAwait(false);
        _ = work.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        Failed(hook, new TimeoutException($"A handler of '{hook}' did not finish within {budget.TotalSeconds:0.#} s and was abandoned."));
        return (false, default!);
    }

    private Registration Add(string hook, object handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(hook);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            var next = new Dictionary<string, object[]>(_handlers, StringComparer.Ordinal)
            {
                [hook] = _handlers.TryGetValue(hook, out var list) ? [.. list, handler] : [handler],
            };
            _handlers = next;
        }

        return new Registration(this, hook, handler);
    }

    private void Remove(string hook, object handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(hook, out var list))
            {
                return;
            }

            var left = list.Where(h => !ReferenceEquals(h, handler)).ToArray();
            var next = new Dictionary<string, object[]>(_handlers, StringComparer.Ordinal);
            if (left.Length == 0)
            {
                next.Remove(hook);
            }
            else
            {
                next[hook] = left;
            }

            _handlers = next;
        }
    }

    private void Failed(string hook, Exception e) => HandlerFailed?.Invoke(this, new HookFailedEventArgs(hook, e));

    private sealed record AnyObserver(Func<object?, CancellationToken, ValueTask> Handler);

    private sealed record AnyInterceptor(Func<object?, CancellationToken, ValueTask<bool>> Handler);

    private sealed class Registration(HookBus bus, string hook, object handler) : IDisposable
    {
        public void Dispose() => bus.Remove(hook, handler);
    }
}
