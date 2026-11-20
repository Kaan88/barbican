// Mirrors: Bulwark lib/plugin-oauth.ts and components/providers/plugin-oauth-callback-listener.tsx.
using System.Security.Cryptography;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.OAuth;

/// <summary>Routes a one-time OAuth state to its authorized background add-on in the current profile.</summary>
public sealed class PluginOAuthCallbacks(IPluginDeliveryAuthority authority, TimeProvider time)
{
    public const string Permission = "http:fetch";
    private const int MaxStates = 256;
    private const int MaxStatesPerOwner = 8;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly Lock _gate = new();
    private readonly Dictionary<Owner, IPluginRuntimeInstance> _receivers = [];
    private readonly Dictionary<string, Pending> _states = new(StringComparer.Ordinal);

    public IDisposable? Register(IPluginRuntimeInstance runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var binding = runtime.Binding;
        if (binding.Kind != PluginFrameKind.Background || binding.Tier != PluginExecutionTier.Untrusted ||
            !runtime.Activation.Hooks.Contains(HookNames.OnOAuthCallback, StringComparer.Ordinal)) return null;
        var registered = false;
        binding.TryWriteWhileAlive(() =>
        {
            lock (_gate) registered = _receivers.TryAdd(Owner.Of(binding), runtime);
        });
        return registered ? new Registration(this, runtime) : null;
    }

    public async Task<string?> CreateStateAsync(PluginRuntimeBinding binding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var key = Owner.Of(binding);
        string? issued = null;
        var accepted = await authority.TryPostAsync(binding, grant =>
        {
            if (!Permitted(grant, binding) || ct.IsCancellationRequested) return false;
            return binding.TryWriteWhileAlive(() =>
            {
                lock (_gate)
                {
                    Prune();
                    if (!_receivers.TryGetValue(key, out var runtime) || !runtime.Binding.IsAlive ||
                        _states.Count >= MaxStates || _states.Values.Count(pending => ReferenceEquals(pending.Receiver, runtime)) >= MaxStatesPerOwner)
                        return;

                    var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
                    if (_states.TryAdd(state, new Pending(runtime, time.GetTimestamp()))) issued = state;
                }
            });
        }, ct).ConfigureAwait(false);
        return accepted ? issued : null;
    }

    /// <summary>Checks whether a current caller owns the supplied state without consuming it.</summary>
    public bool OwnsState(PluginRuntimeBinding binding, string state)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.IsAlive || !ValidState(state)) return false;
        lock (_gate)
        {
            return _states.TryGetValue(state, out var pending) && IsCurrent(pending) &&
                Owner.Of(binding) == Owner.Of(pending.Receiver.Binding);
        }
    }

    internal bool TryClaimLaunch(PluginRuntimeBinding binding, string state)
    {
        lock (_gate)
        {
            if (!OwnsState(binding, state) || _states[state].Launched) return false;
            _states[state] = _states[state] with { Launched = true };
            return true;
        }
    }

    public async Task<bool> DeliverAsync(PluginOAuthCallback callback, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ct.ThrowIfCancellationRequested();
        if (!ValidCallback(callback)) return false;
        IPluginRuntimeInstance receiver;
        lock (_gate)
        {
            if (!_states.Remove(callback.State, out var pending) || !IsCurrent(pending)) return false;
            receiver = pending.Receiver;
        }

        var binding = receiver.Binding;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
        try
        {
            if (!await authority.TryPostAsync(binding, grant => Permitted(grant, binding) && !stop.IsCancellationRequested && IsRegistered(receiver),
                stop.Token).ConfigureAwait(false)) return false;

            var arguments = Project(callback, time.GetUtcNow());
            var result = await receiver.InvokeHookAsync(HookNames.OnOAuthCallback, arguments,
                HookPolicies.For(HookNames.OnOAuthCallback).Budget, stop.Token).ConfigureAwait(false);
            return result.IsOk && binding.IsAlive && !stop.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && stop.IsCancellationRequested) { return false; }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException) { return false; }
    }

    private void Prune()
    {
        foreach (var state in _states.Where(entry => !IsCurrent(entry.Value)).Select(entry => entry.Key).ToArray())
            _states.Remove(state);
    }

    private bool IsCurrent(Pending pending) =>
        pending.Receiver.Binding.IsAlive && time.GetElapsedTime(pending.Created) < Lifetime;

    private bool IsRegistered(IPluginRuntimeInstance runtime)
    {
        lock (_gate) return _receivers.TryGetValue(Owner.Of(runtime.Binding), out var registered) && ReferenceEquals(runtime, registered);
    }

    private void Unregister(IPluginRuntimeInstance runtime)
    {
        lock (_gate)
        {
            var key = Owner.Of(runtime.Binding);
            if (!_receivers.TryGetValue(key, out var registered) || !ReferenceEquals(registered, runtime)) return;
            _receivers.Remove(key);
            foreach (var state in _states.Where(entry => ReferenceEquals(entry.Value.Receiver, runtime)).Select(entry => entry.Key).ToArray())
                _states.Remove(state);
        }
    }

    private static bool Permitted(PluginGrantSnapshot grant, PluginRuntimeBinding binding) =>
        binding.IsAlive && grant.Enabled && grant.Package == binding.Package && grant.Generation == binding.GrantGeneration &&
        grant.Tier == binding.Tier && grant.Tier == PluginExecutionTier.Untrusted && grant.Permissions.Contains(Permission);

    private static bool ValidState(string? state) => state is { Length: 48 } && state.All(char.IsAsciiHexDigitLower);

    private static bool ValidCallback(PluginOAuthCallback callback) => ValidState(callback.State) &&
        (callback.Error is null
            ? callback.ErrorDescription is null && ValidText(callback.Code, 4096)
            : callback.Code is null && ValidText(callback.Error, 256) &&
              (callback.ErrorDescription is null or "" || ValidText(callback.ErrorDescription, 1024)));

    private static bool ValidText(string? value, int limit) =>
        value is { Length: > 0 } && value.Length <= limit && !value.Any(char.IsControl);

    private static PluginValue Project(PluginOAuthCallback callback, DateTimeOffset receivedAt)
    {
        var builder = new PluginValueBuilder();
        var properties = new List<(string, int)>
        {
            ("state", builder.String(callback.State)),
            ("receivedAt", builder.Number(receivedAt.ToUnixTimeMilliseconds())),
        };
        if (callback.Code is { } code) properties.Add(("code", builder.String(code)));
        else
        {
            properties.Add(("error", builder.String(callback.Error!)));
            if (callback.ErrorDescription is { } description)
                properties.Add(("error_description", builder.String(description)));
        }
        return builder.Build(builder.Array(builder.Object([.. properties])));
    }

    private sealed record Owner(string Profile, PluginPackageIdentity Package, long Generation)
    {
        public static Owner Of(PluginRuntimeBinding binding) => new(binding.Profile, binding.Package, binding.GrantGeneration);
    }

    private sealed record Pending(IPluginRuntimeInstance Receiver, long Created, bool Launched = false);

    private sealed class Registration(PluginOAuthCallbacks callbacks, IPluginRuntimeInstance receiver) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) callbacks.Unregister(receiver);
        }
    }
}
