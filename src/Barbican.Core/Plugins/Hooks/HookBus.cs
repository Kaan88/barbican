// Mirrors: Bulwark lib/plugin-hook-catalog.ts (observer / intercept / transform), lib/plugin-mail-hooks.ts (dispatch order, a failing
// handler never stops the app), PORT-PLAN.md M6b (host-agnostic bus first, plugin hosts later); M1-plan §4.1. Owner mailcore.

namespace Barbican.Core.Plugins.Hooks;

/// <summary>
/// The in-process hook bus. Handlers are registered by tests today and by the plugin hosts in M6b. With no handler for a hook
/// every call is a dictionary lookup and returns a completed task: no allocation, so call sites cost nothing without plugins.
/// </summary>
public sealed class HookBus : IHookBus
{
    /// <summary>How long one handler may take before the bus gives up on it (Fable F-15): a plugin that never returns must not stop the sync.</summary>
    public static readonly TimeSpan DefaultHandlerTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;
    private readonly Lock _gate = new();
    // copy on write: a registration builds a new dictionary and swaps it in, so readers (the hot path) never lock and never allocate
    private volatile Dictionary<string, Delegate[]> _handlers = new(StringComparer.Ordinal);

    /// <summary>Creates a bus; a handler that takes longer than <paramref name="handlerTimeout"/> is abandoned and reported through <see cref="HandlerFailed"/>.</summary>
    public HookBus(TimeProvider? time = null, TimeSpan? handlerTimeout = null)
    {
        _time = time ?? TimeProvider.System;
        _timeout = handlerTimeout ?? DefaultHandlerTimeout;
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

    /// <inheritdoc />
    public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) =>
        _handlers.TryGetValue(hook, out var list) ? NotifyCoreAsync(hook, list, payload, ct) : default;

    private async ValueTask NotifyCoreAsync<TPayload>(string hook, Delegate[] list, TPayload payload, CancellationToken ct)
    {
        foreach (var d in list)
        {
            if (d is not Func<TPayload, CancellationToken, ValueTask> observer)
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

    private async ValueTask<bool> InterceptCoreAsync<TPayload>(string hook, Delegate[] list, TPayload payload, CancellationToken ct)
    {
        foreach (var d in list)
        {
            if (d is not Func<TPayload, CancellationToken, ValueTask<bool>> interceptor)
            {
                continue;
            }

            try
            {
                // a handler that timed out counts as "allow"
                var run = await RunAsync(hook, async t => await interceptor(payload, t).ConfigureAwait(false), ct).ConfigureAwait(false);
                if (run.Done && !run.Value)
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
            }
        }

        return true;
    }

    /// <inheritdoc />
    public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) =>
        _handlers.TryGetValue(hook, out var list) ? TransformCoreAsync(hook, list, value, ct) : ValueTask.FromResult(value);

    private async ValueTask<T> TransformCoreAsync<T>(string hook, Delegate[] list, T value, CancellationToken ct)
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

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(_timeout, _time, delayCts.Token);
        var first = await Task.WhenAny(work, delay).ConfigureAwait(false);
        if (ReferenceEquals(first, work))
        {
            await delayCts.CancelAsync().ConfigureAwait(false);
            return (true, await work.ConfigureAwait(false));
        }

        ct.ThrowIfCancellationRequested();
        await handlerCts.CancelAsync().ConfigureAwait(false);
        _ = work.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        Failed(hook, new TimeoutException($"A handler of '{hook}' did not finish within {_timeout.TotalSeconds:0.#} s and was abandoned."));
        return (false, default!);
    }

    private Registration Add(string hook, Delegate handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(hook);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            var next = new Dictionary<string, Delegate[]>(_handlers, StringComparer.Ordinal)
            {
                [hook] = _handlers.TryGetValue(hook, out var list) ? [.. list, handler] : [handler],
            };
            _handlers = next;
        }

        return new Registration(this, hook, handler);
    }

    private void Remove(string hook, Delegate handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(hook, out var list))
            {
                return;
            }

            var left = list.Where(h => !ReferenceEquals(h, handler)).ToArray();
            var next = new Dictionary<string, Delegate[]>(_handlers, StringComparer.Ordinal);
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

    private sealed class Registration(HookBus bus, string hook, Delegate handler) : IDisposable
    {
        public void Dispose() => bus.Remove(hook, handler);
    }
}
