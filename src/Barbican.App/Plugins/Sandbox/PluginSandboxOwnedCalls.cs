// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:318-322,521-541,614-666,718-726,740-763.
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Per-frame reusable prop capabilities, one-shot replies and multi-chunk streams.</summary>
/// <remarks>Callers must recheck binding and current authority immediately before side effects and after awaits.</remarks>
public sealed class PluginSandboxOwnedCalls : IDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _callbacks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _streams = new(StringComparer.Ordinal);
    // Never resurrect an old wire ID within this instance; cap lifetime history to bound memory.
    private readonly HashSet<string> _usedCallbacks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedPending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedStreams = new(StringComparer.Ordinal);
    private readonly int _historyLimit;
    private bool _disposed;
    private long _hookSequence;

    public PluginSandboxOwnedCalls(PluginRuntimeBinding owner, int maxCallbacks, int maxPending, int maxStreams)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCallbacks);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPending);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStreams);
        Owner = owner;
        MaxCallbacks = maxCallbacks;
        MaxPending = maxPending;
        MaxStreams = maxStreams;
        _historyLimit = (int)Math.Min(65_536L, Math.Max(1024L, Math.Max(maxCallbacks, Math.Max(maxPending, maxStreams)) * 64L));
    }
    public PluginRuntimeBinding Owner { get; }
    public int MaxCallbacks { get; }
    public int MaxPending { get; }
    public int MaxStreams { get; }
    private bool Active => !_disposed && Owner.IsAlive;
    private bool Authorized(PluginRuntimeBinding claimant) => Active && ReferenceEquals(Owner, claimant);
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 256 } && !id.Any(char.IsControl);

    /// <summary>Replaces prop IDs atomically; failure leaves previous IDs available.</summary>
    public bool ReplacePropCallbacks(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_gate)
        {
            if (!Active || ids.Count > MaxCallbacks || (long)_usedCallbacks.Count + ids.Count > _historyLimit) return false;
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids) if (!ValidId(id) || !next.Add(id) || _usedCallbacks.Contains(id)) return false;
            _callbacks.Clear();
            _callbacks.UnionWith(next);
            _usedCallbacks.UnionWith(next);
            return true;
        }
    }
    public bool CanInvokePropCallback(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _callbacks.Contains(id);
    }
    public bool AddPendingReply(string id)
    {
        lock (_gate) return !IsHookId(id) && Add(_pending, _usedPending, id, MaxPending);
    }
    // This namespace is minted only by the host; a high-water mark prevents reuse without
    // retaining a tombstone for every hook request in a long-lived runtime.
    private string HookPrefix => string.Concat(Owner.InstanceId.ToString("N"), ":h:");
    private bool IsHookId(string? id) => id is not null && id.StartsWith(HookPrefix, StringComparison.Ordinal);
    internal bool AddHookReply(long sequence, out string id)
    {
        lock (_gate)
        {
            id = string.Empty;
            if (!Active || sequence <= _hookSequence || _pending.Count >= MaxPending) return false;
            id = string.Concat(HookPrefix, sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!_pending.Add(id)) return false;
            _hookSequence = sequence;
            return true;
        }
    }
    public bool TakePendingReply(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _pending.Remove(id);
    }
    public bool BeginStream(string id)
    {
        lock (_gate) return Add(_streams, _usedStreams, id, MaxStreams);
    }
    public bool AcceptStreamChunk(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _streams.Contains(id);
    }
    public bool EndStream(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _streams.Remove(id);
    }
    private bool Add(HashSet<string> active, HashSet<string> used, string id, int limit)
    {
        if (!Active || !ValidId(id) || active.Count >= limit || used.Count >= _historyLimit || !used.Add(id)) return false;
        return active.Add(id);
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _callbacks.Clear(); _pending.Clear(); _streams.Clear();
            _usedCallbacks.Clear(); _usedPending.Clear(); _usedStreams.Clear();
        }
    }
}