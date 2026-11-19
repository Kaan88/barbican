// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2067-2087 and api-timeouts.ts.
// Host-native caller supplies the authenticated binding. Adapters retain per-method validation, projection and resource routing.
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Bridge;

public sealed class PluginApiDispatcher : IPluginApiDispatcher
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, int> _pending = [];
    private readonly Dictionary<string, IPluginApiMethod> _methods;
    private readonly IPluginAuthority _authority;
    private readonly IPluginSessionSource _sessions;
    private readonly string _profile;
    private readonly TimeProvider _time;
    private readonly int _maxPerInstance;
    private readonly int _maxTotal;
    private int _total;

    public PluginApiDispatcher(string profile, IEnumerable<IPluginApiMethod> methods, IPluginAuthority authority,
        IPluginSessionSource sessions, TimeProvider? time = null, int maxPerInstance = 16, int maxTotal = 128)
    {
        if (!Storage.ProfilePaths.IsValidName(profile)) throw new ArgumentException("Invalid host profile.", nameof(profile));
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPerInstance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTotal);
        _profile = profile;
        _methods = methods.ToDictionary(x => x.Descriptor.Name, StringComparer.Ordinal);
        _authority = authority;
        _sessions = sessions;
        _time = time ?? TimeProvider.System;
        _maxPerInstance = maxPerInstance;
        _maxTotal = maxTotal;
    }

    public async Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        if (!binding.IsAlive || !string.Equals(binding.Profile, _profile, StringComparison.Ordinal)) return Fail(PluginErrorCode.StaleContext);
        if (!_methods.TryGetValue(request.Method, out var method)) return Fail(PluginErrorCode.UnknownMethod);
        if (!Reserve(binding.InstanceId)) return Fail(PluginErrorCode.RateLimited);
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
        var lifetime = stop.Token;
        var state = new CallState();
        // A synchronous misbehaving adapter cannot block the UI or bypass the caller's deadline.
        var work = Task.Run(() => ExecuteAsync(binding, request, method, state, stop), CancellationToken.None);
        try { return await work.WaitAsync(method.Descriptor.Deadline, _time, lifetime).ConfigureAwait(false); }
        catch (TimeoutException) { return Interrupted(state, stop, PluginErrorCode.Timeout); }
        catch (OperationCanceledException) { return Interrupted(state, stop, PluginErrorCode.Cancelled); }
    }
    private static PluginCallOutcome Interrupted(CallState state, CancellationTokenSource stop, PluginErrorCode code)
    {
        var issued = state.Close();
        _ = CancelAsync(stop);
        return Fail(issued ? PluginErrorCode.UnknownOutcome : code);
    }
    private async Task<PluginCallOutcome> ExecuteAsync(PluginRuntimeBinding binding, PluginApiRequest request, IPluginApiMethod method,
        CallState state, CancellationTokenSource stop)
    {
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            var current = await _authority.GetCurrentAsync(binding.Package.Id, stop.Token).ConfigureAwait(false);
            if (!current.IsOk || current.Value is null || !PluginInvocationContext.Matches(current.Value, binding)) return Fail(PluginErrorCode.PermissionDenied);
            var grant = current.Value;
            var descriptor = method.Descriptor;
            if (descriptor.PrivilegedOnly && grant.Tier == PluginExecutionTier.Untrusted) return Fail(PluginErrorCode.PermissionDenied);
            if (!descriptor.PermissionAlternatives.IsEmpty && !descriptor.PermissionAlternatives.Any(group => group.All(grant.Permissions.Contains))) return Fail(PluginErrorCode.PermissionDenied);
            if (method.ValidateArguments(request.Arguments) is { } invalid) return Fail(invalid);
            var context = new PluginInvocationContext(binding, grant, _authority, _sessions, descriptor.MayMutate, stop.Token);
            if (!state.SetContext(context)) return Fail(PluginErrorCode.Cancelled);
            if (!await context.CheckCurrentAsync(stop.Token).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
            var outcome = await method.InvokeAsync(context, request.Arguments, stop.Token).ConfigureAwait(false);
            if (!await context.CheckCurrentAsync(stop.Token).ConfigureAwait(false)) return Fail(context.HasIssuedMutation ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext);
            return outcome;
        }
        catch (OperationCanceledException) { return Fail(state.Close() ? PluginErrorCode.UnknownOutcome : PluginErrorCode.Cancelled); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { return Fail(state.Close() ? PluginErrorCode.UnknownOutcome : PluginErrorCode.Failed); }
        finally
        {
            state.Close();
            stop.Dispose();
            Release(binding.InstanceId);
        }
    }
    private bool Reserve(Guid id)
    {
        lock (_gate)
        {
            var count = _pending.GetValueOrDefault(id);
            if (count >= _maxPerInstance || _total >= _maxTotal) return false;
            _pending[id] = count + 1;
            _total++;
            return true;
        }
    }
    private void Release(Guid id)
    {
        lock (_gate)
        {
            if (_pending[id] == 1) _pending.Remove(id); else _pending[id]--;
            _total--;
        }
    }
    private static async Task CancelAsync(CancellationTokenSource stop)
    {
        try { await stop.CancelAsync().ConfigureAwait(false); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
    }
    private static PluginCallOutcome Fail(PluginErrorCode code) => PluginCallOutcome.Failed(code);
    private sealed class CallState
    {
        private readonly Lock _gate = new();
        private PluginInvocationContext? _context;
        private bool _closed;
        public bool SetContext(PluginInvocationContext context)
        {
            lock (_gate) { if (_closed) { context.Close(); return false; } _context = context; return true; }
        }
        public bool Close() { lock (_gate) { _closed = true; return _context?.Close() ?? false; } }
    }
}
