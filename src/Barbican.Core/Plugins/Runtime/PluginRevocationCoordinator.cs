// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts disposal and stores/plugin-store.ts disable/uninstall.
// Host-only resource bookkeeping, not an authority source: factories still fetch current grants before execution.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Runtime;

public sealed class PluginRevocationCoordinator : IPluginRevocationCoordinator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<PluginPackageIdentity, long> _revoked = [];
    private readonly string _profile;
    private readonly TimeProvider _time;
    private readonly TimeSpan _budget;
    private readonly int _maxResources;
    private bool _closed;

    public PluginRevocationCoordinator(string profile, TimeProvider? time = null, TimeSpan? teardownBudget = null, int maxResources = 4096)
    {
        if (!ProfilePaths.IsValidName(profile)) throw new ArgumentException("Invalid host profile.", nameof(profile));
        var budget = teardownBudget ?? TimeSpan.FromSeconds(5);
        if (budget <= TimeSpan.Zero || budget.TotalMilliseconds >= uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(teardownBudget));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResources);
        _profile = profile;
        _time = time ?? TimeProvider.System;
        _budget = budget;
        _maxResources = maxResources;
    }

    internal int TrackedCount { get { lock (_gate) return _entries.Count; } }

    public Result<IDisposable> Track(PluginRuntimeBinding binding, Func<CancellationToken, ValueTask> teardown)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(teardown);
        if (!string.Equals(binding.Profile, _profile, StringComparison.Ordinal)) return Refused<IDisposable>("Foreign plugin profile.");
        lock (_gate)
        {
            var stale = _closed || !binding.IsAlive || (_revoked.TryGetValue(binding.Package, out var generation) && binding.GrantGeneration <= generation);
            if (!stale)
            {
                if (_entries.Count >= _maxResources) return Refused<IDisposable>("Plugin resource limit reached.");
                var entry = new Entry(binding, teardown);
                _entries.Add(entry.Id, entry);
                return Result<IDisposable>.Ok(new Registration(this, entry.Id));
            }
        }
        _ = CloseBindingAsync(binding);
        return Refused<IDisposable>("Plugin generation has been revoked.");
    }

    public Task<Result<Unit>> RevokeAsync(PluginPackageIdentity package, long throughGeneration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentOutOfRangeException.ThrowIfNegative(throughGeneration);
        var (entries, starts) = Prepare(package, throughGeneration);
        return StopAsync(entries, starts, ct);
    }

    /// <summary>Closes intake and invalidates every binding before waiting for bounded teardown.</summary>
    public Task<Result<Unit>> CloseAsync(CancellationToken ct)
    {
        Entry[] entries;
        List<(Entry Entry, TaskCompletionSource<bool> Completion, CancellationTokenSource Cancellation)> starts;
        lock (_gate)
        {
            _closed = true;
            entries = [.. _entries.Values];
            starts = PrepareTeardown(entries);
        }
        return StopAsync(entries, starts, ct);
    }

    private async Task<Result<Unit>> StopAsync(Entry[] entries,
        List<(Entry Entry, TaskCompletionSource<bool> Completion, CancellationTokenSource Cancellation)> starts, CancellationToken ct)
    {
        // Close synchronously before any cleanup await; cancellation only cancels the caller's wait, never this barrier.
        foreach (var (entry, completion, cancellation) in starts)
        {
            _ = CompleteAttemptAsync(entry, completion, cancellation);
        }
        var cleanup = Task.WhenAll(entries.Select(ObserveAttemptAsync));
        try
        {
            var outcomes = await cleanup.WaitAsync(ct).ConfigureAwait(false);
            return outcomes.All(x => x) ? Result<Unit>.Ok(default) : Refused<Unit>("Some plugin resources could not be stopped; cleanup remains pending.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Refused<Unit>("Plugin cleanup continues; its wait was cancelled.");
        }
    }

    private (Entry[] Entries, List<(Entry Entry, TaskCompletionSource<bool> Completion, CancellationTokenSource Cancellation)> Starts) Prepare(PluginPackageIdentity package, long generation)
    {
        lock (_gate)
        {
            var cutoff = _revoked.TryGetValue(package, out var previous) ? Math.Max(previous, generation) : generation;
            _revoked[package] = cutoff;
            var entries = _entries.Values.Where(e => e.Binding.Package == package && e.Binding.GrantGeneration <= cutoff).ToArray();
            return (entries, PrepareTeardown(entries));
        }
    }

    private static List<(Entry, TaskCompletionSource<bool>, CancellationTokenSource)> PrepareTeardown(Entry[] entries)
    {
        // No callbacks, waits or native calls under the coordinator lock.
        foreach (var entry in entries) entry.Binding.Invalidate();
        var starts = new List<(Entry, TaskCompletionSource<bool>, CancellationTokenSource)>();
        foreach (var entry in entries)
        {
            if (entry.Work is not null && (!entry.Work.IsCompleted || !entry.Failed)) continue;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellation = new CancellationTokenSource();
            entry.Work = completion.Task;
            entry.Cancellation = cancellation;
            entry.Failed = false;
            starts.Add((entry, completion, cancellation));
        }
        return starts;
    }

    private async Task CompleteAttemptAsync(Entry entry, TaskCompletionSource<bool> completion, CancellationTokenSource cancellation)
    {
        var success = false;
        try
        {
            var closing = CloseBindingAsync(entry.Binding);
            var cleanup = Task.Run(async () => await entry.Teardown(cancellation.Token).ConfigureAwait(false), CancellationToken.None);
            await Task.WhenAll(closing, cleanup).ConfigureAwait(false);
            success = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
        finally
        {
            lock (_gate)
            {
                entry.Failed = !success;
                entry.Cancellation = null;
                if (success) _entries.Remove(entry.Id);
            }
            cancellation.Dispose();
            completion.TrySetResult(success);
        }
    }

    private async Task<bool> ObserveAttemptAsync(Entry entry)
    {
        Task<bool> work;
        lock (_gate) work = entry.Work!;
        try { return await work.WaitAsync(_budget, _time, CancellationToken.None).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            CancellationTokenSource? cancellation;
            lock (_gate) cancellation = entry.Cancellation;
            if (cancellation is not null) _ = CancelAttemptAsync(cancellation);
            return false;
        }
    }

    private static async Task CancelAttemptAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
    }

    private static async Task CloseBindingAsync(PluginRuntimeBinding binding)
    {
        // A notification callback fault cannot skip explicit teardown; a still-running callback remains bounded by the observer budget.
        try { await binding.DisposeAsync().ConfigureAwait(false); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
    }

    private void Remove(Guid id)
    {
        lock (_gate)
        {
            // Once revocation owns cleanup, retain failures even when the owner detaches its registration.
            if (_entries.TryGetValue(id, out var entry) && entry.Work is null) _entries.Remove(id);
        }
    }

    private static Result<T> Refused<T>(string message) => Result<T>.Fail(new ValidationError(message));

    private sealed class Entry(PluginRuntimeBinding binding, Func<CancellationToken, ValueTask> teardown)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public PluginRuntimeBinding Binding { get; } = binding;
        public Func<CancellationToken, ValueTask> Teardown { get; } = teardown;
        public Task<bool>? Work { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public bool Failed { get; set; }
    }

    private sealed class Registration(PluginRevocationCoordinator owner, Guid id) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(id); }
    }
}
