// Mirrors: Rust plugins.rs:29 Plugin (the loaded library kept with its manifest); M2 plan 6.5 (a collectible AssemblyLoadContext per plugin). Owner unified.

using System.Runtime.Loader;
using Barbican.Core.Plugins.Runtime;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>One plugin the host holds: what it lists, the instance, its load context, its hook subscriptions and its start.</summary>
internal sealed class LoadedPlugin(PluginInfo info, IBarbicanPlugin plugin, AssemblyLoadContext? context, PluginRuntimeBinding? binding = null)
{
    private int _published;
    private readonly Lock _calls = new();
    private TaskCompletionSource? _drained;
    private int _inFlight;
    public PluginInfo Info { get; } = info;

    public IBarbicanPlugin Plugin { get; } = plugin;

    /// <summary>The plugin's own load context; null for an in-process plugin (tests).</summary>
    public AssemblyLoadContext? Context { get; } = context;
    public CancellationToken Lifetime => binding?.Lifetime ?? CancellationToken.None;
    public bool IsArchive => binding is not null;
    public bool AcceptsCalls => binding is null || (binding.IsAlive && Volatile.Read(ref _published) != 0);
    public void Publish() => Volatile.Write(ref _published, 1);

    public bool TryEnterCall()
    {
        lock (_calls)
        {
            if (!AcceptsCalls || _drained is not null) return false;
            _inFlight++;
            return true;
        }
    }

    public void ExitCall()
    {
        lock (_calls)
        {
            if (--_inFlight == 0) _drained?.TrySetResult();
        }
    }

    public Task DrainCallsAsync()
    {
        lock (_calls)
        {
            _drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inFlight == 0) _drained.TrySetResult();
#pragma warning disable VSTHRD003 // Worker completion signals this task without a synchronization context.
            return _drained.Task;
#pragma warning restore VSTHRD003
        }
    }

    /// <summary>The plugin's hook subscriptions on the app's bus (set right after loading).</summary>
    public PluginHookBridge Bridge { get; set; } = null!;

    /// <summary>Completes when InitializeAsync returned; faulted when the plugin could not start (it is then never called).</summary>
    public Task Ready { get; set; } = Task.CompletedTask;
}
