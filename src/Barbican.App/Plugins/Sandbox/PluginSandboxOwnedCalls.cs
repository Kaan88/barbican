// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:318-322,521-541,614-666,718-726,740-763.
using System.Globalization;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Per-frame reusable prop capabilities, one-shot replies and multi-chunk streams.</summary>
/// <remarks>Only the host mints IDs. Plugin-origin request IDs need a separate active correlation table.
/// Callers recheck binding and current authority before side effects and after awaits.</remarks>
public sealed class PluginSandboxOwnedCalls : IDisposable
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _callbacks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _streams = new(StringComparer.Ordinal);
    private long _nextId;
    private bool _disposed;
    public PluginSandboxOwnedCalls(PluginRuntimeBinding owner, int maxCallbacks, int maxPending, int maxStreams)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCallbacks);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPending);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStreams);
        Owner = owner; MaxCallbacks = maxCallbacks; MaxPending = maxPending; MaxStreams = maxStreams;
    }
    public PluginRuntimeBinding Owner { get; }
    public int MaxCallbacks { get; }
    public int MaxPending { get; }
    public int MaxStreams { get; }
    private bool Active => !_disposed && Owner.IsAlive;
    private bool Authorized(PluginRuntimeBinding claimant) => Active && ReferenceEquals(Owner, claimant);
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 256 } && !id.Any(char.IsControl);
    private bool CanMint(int count)
    {
        if (!Active) return false;
        if (_nextId <= long.MaxValue - (long)count) return true;
        // The binding closes before another ID could be minted; the caller must create a new frame.
        _ = Owner.DisposeAsync().AsTask();
        return false;
    }
    private string Mint(char category) => string.Concat(Owner.InstanceId.ToString("N"), ":", category, ":", (++_nextId).ToString(CultureInfo.InvariantCulture));
    /// <summary>Atomically issues a fresh prop generation; failure preserves the old generation.</summary>
    public bool TryReplacePropCallbacks(int count, out IReadOnlyList<string> ids)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            ids = Array.Empty<string>();
            if (count > MaxCallbacks || !CanMint(count)) return false;
            var next = new string[count];
            for (var i = 0; i < count; i++) next[i] = Mint('c');
            _callbacks.Clear(); _callbacks.UnionWith(next);
            ids = Array.AsReadOnly(next);
            return true;
        }
    }
    public bool CanInvokePropCallback(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _callbacks.Contains(id);
    }
    public bool TryAddPendingReply(out string id)
    {
        lock (_gate) return TryAdd(_pending, MaxPending, 'p', out id);
    }
    public bool TakePendingReply(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _pending.Remove(id);
    }
    public bool TryBeginStream(out string id)
    {
        lock (_gate) return TryAdd(_streams, MaxStreams, 's', out id);
    }
    public bool AcceptStreamChunk(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _streams.Contains(id);
    }
    public bool EndStream(PluginRuntimeBinding claimant, string id)
    {
        lock (_gate) return Authorized(claimant) && ValidId(id) && _streams.Remove(id);
    }
    private bool TryAdd(HashSet<string> active, int limit, char category, out string id)
    {
        id = string.Empty;
        if (active.Count >= limit || !CanMint(1)) return false;
        id = Mint(category);
        return active.Add(id);
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _callbacks.Clear(); _pending.Clear(); _streams.Clear();
        }
    }
}
