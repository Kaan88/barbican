// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts instance ownership and disposal.
// Host-created object identity; native transport must authenticate its actual frame before passing this object to a broker.
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Storage;
namespace Barbican.Core.Plugins.Runtime;
public sealed class PluginRuntimeBinding : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _lifetime;
    private readonly Lock _gate = new();
    private readonly Lock _deliveryGate = new();
    private Task? _closing;
    private int _closed;
    private PluginRuntimeBinding(string profile, PluginGrantSnapshot grant, PluginFrameKind kind, string? slot)
    {
        Profile = profile;
        Package = grant.Package;
        GrantGeneration = grant.Generation;
        Tier = grant.Tier;
        Kind = kind;
        Slot = slot;
        InstanceId = Guid.NewGuid();
        _lifetime = _stop.Token;
    }
    public static PluginRuntimeBinding Create(string profile, PluginGrantSnapshot grant, PluginFrameKind kind, string? slot = null)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (!ProfilePaths.IsValidName(profile)) throw new ArgumentException("A host profile identifier is required.", nameof(profile));
        if (!grant.Enabled) throw new ArgumentException("Disabled authority cannot create a runtime.", nameof(grant));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if ((kind == PluginFrameKind.Slot && string.IsNullOrWhiteSpace(slot)) || (kind == PluginFrameKind.Background && slot is not null))
            throw new ArgumentException("Frame kind and slot disagree.", nameof(slot));
        return new PluginRuntimeBinding(profile, grant, kind, slot);
    }
    public Guid InstanceId { get; }
    public string Profile { get; }
    public PluginPackageIdentity Package { get; }
    public long GrantGeneration { get; }
    public PluginExecutionTier Tier { get; }
    public PluginFrameKind Kind { get; }
    public string? Slot { get; }
    public CancellationToken Lifetime => _lifetime;
    public bool IsAlive => Volatile.Read(ref _closed) == 0;
    // The coordinator can establish the entire generation barrier before scheduling any user cleanup.
    internal void Invalidate()
    {
        lock (_deliveryGate) Volatile.Write(ref _closed, 1);
    }

    /// <summary>Orders a synchronous native write against binding invalidation.</summary>
    public bool TryWriteWhileAlive(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_deliveryGate)
        {
            if (!IsAlive) return false;
            write();
            return true;
        }
    }

    /// <summary>Orders one synchronous write against invalidation of all participating bindings.</summary>
    public static bool TryWriteWhileAllAlive(IEnumerable<PluginRuntimeBinding> bindings, Func<bool> write)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(write);
        var ordered = bindings.Distinct().OrderBy(binding => binding.InstanceId).ToArray();
        var held = 0;
        try
        {
            foreach (var binding in ordered)
            {
                binding._deliveryGate.Enter();
                held++;
                if (!binding.IsAlive) return false;
            }
            return write();
        }
        finally
        {
            while (held > 0) ordered[--held]._deliveryGate.Exit();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_closing ??= CloseCoreAsync());
    }
    private async Task CloseCoreAsync()
    {
        Invalidate();
        try { await _stop.CancelAsync().ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
