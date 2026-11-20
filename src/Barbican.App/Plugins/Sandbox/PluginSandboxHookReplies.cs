// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:315-316,538-548,614-644,750-760.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Correlates host-issued hook requests with one authenticated frame's replies.</summary>
internal sealed class PluginSandboxHookReplies : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly PluginRuntimeBinding _owner;
    private readonly PluginSandboxOwnedCalls _owned;
    private long _sequence;
    private bool _closed;

    internal PluginSandboxHookReplies(PluginRuntimeBinding owner, int maxPending = 128)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPending);
        _owner = owner;
        _owned = new PluginSandboxOwnedCalls(owner, 0, maxPending, 0);
    }

    internal bool TryBegin(CancellationToken cancellationToken, out string id, out Task<PluginValue> reply)
    {
        Pending entry;
        lock (_gate)
        {
            id = string.Empty;
            reply = Task.FromCanceled<PluginValue>(new CancellationToken(true));
            if (_closed || !_owner.IsAlive || cancellationToken.IsCancellationRequested || _pending.Count >= _owned.MaxPending || _sequence == long.MaxValue)
                return false;
            if (!_owned.AddHookReply(++_sequence, out id)) return false;
            entry = new Pending(id, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _owner.Lifetime));
            _pending.Add(id, entry);
            entry.Registration = entry.Stop.Token.Register(() => Cancel(entry));
            reply = entry.Completion.Task;
        }
        _ = CleanupAsync(entry);
        return true;
    }

    /// <summary>Called immediately before the actual native post, under the host's delivery fence.</summary>
    internal bool TryMarkPosted(string id)
    {
        lock (_gate)
        {
            if (_closed || !_owner.IsAlive || !_pending.TryGetValue(id, out var entry) || entry.Posted || entry.Stop.IsCancellationRequested)
                return false;
            entry.Posted = true;
            return true;
        }
    }

    internal bool TryComplete(PluginRuntimeBinding claimant, string id, PluginValue validatedEnvelope)
    {
        if (!ReferenceEquals(claimant, _owner)) return false;
        lock (_gate)
        {
            if (_closed || !_owner.IsAlive || !_pending.TryGetValue(id, out var entry) || !entry.Posted || entry.Stop.IsCancellationRequested)
                return false;
            if (!_owned.TakePendingReply(claimant, id)) return false;
            _pending.Remove(id);
            entry.Completion.TrySetResult(validatedEnvelope);
            return true;
        }
    }

    private void Cancel(Pending entry)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(entry.Id, out var current) || !ReferenceEquals(current, entry)) return;
            _owned.TakePendingReply(_owner, entry.Id);
            _pending.Remove(entry.Id);
            entry.Completion.TrySetCanceled(entry.Stop.Token);
        }
    }

    private static async Task CleanupAsync(Pending entry)
    {
#pragma warning disable VSTHRD003 // Completion is a rendezvous from native ingress or cancellation and never captures a UI context.
        try { await entry.Completion.Task.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
        catch (OperationCanceledException) { }
        finally
        {
            await entry.Registration.DisposeAsync().ConfigureAwait(false);
            entry.Stop.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _owned.Dispose();
            foreach (var entry in _pending.Values) entry.Completion.TrySetCanceled();
            _pending.Clear();
        }
    }

    private sealed class Pending(string id, CancellationTokenSource stop)
    {
        internal string Id { get; } = id;
        internal CancellationTokenSource Stop { get; } = stop;
        internal TaskCompletionSource<PluginValue> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Registration { get; set; }
        internal bool Posted { get; set; }
    }
}
