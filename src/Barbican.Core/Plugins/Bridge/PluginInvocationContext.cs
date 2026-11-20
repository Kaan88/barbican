// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:25-30 and host-api.ts:2067-2087.
// Barbican adds live grant and captured-session fences; adapters still enforce resource schemas and backend revisions.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Bridge;

internal sealed class PluginInvocationContext : IPluginInvocationContext
{
    private readonly Lock _gate = new();
    private readonly List<PluginSessionCapture> _captures = [];
    private PluginSessionCapture? _active;
    private bool _closed;
    private bool _checked;
    private bool _issued;
    private readonly IPluginAuthority _authority;
    private readonly IPluginSessionSource _sessions;
    private readonly bool _mayMutate;
    private readonly CancellationToken _lifetime;
    public PluginInvocationContext(PluginRuntimeBinding binding, PluginGrantSnapshot grant, IPluginAuthority authority,
        IPluginSessionSource sessions, bool mayMutate, CancellationToken lifetime)
    {
        Binding = binding;
        Grant = grant;
        _authority = authority;
        _sessions = sessions;
        _mayMutate = mayMutate;
        _lifetime = lifetime;
        _authority.Changed += OnAuthorityChanged;
    }
    public PluginRuntimeBinding Binding { get; }
    public PluginGrantSnapshot Grant { get; }
    public bool HasIssuedMutation { get { lock (_gate) return _issued; } }

    public Result<PluginSessionCapture> CaptureActive()
    {
        lock (_gate)
        {
            if (!IsLive()) return Refused<PluginSessionCapture>();
            if (_active is not null) return Result<PluginSessionCapture>.Ok(_active);
            var capture = Capture(_sessions.CaptureActive());
            if (capture.IsOk) _active = capture.Value;
            return capture;
        }
    }
    public Result<PluginSessionCapture> CaptureOwner(string localAccountKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        lock (_gate)
        {
            if (!IsLive() || !Grant.AccountKeys.Contains(localAccountKey)) return Refused<PluginSessionCapture>();
            var previous = _captures.Find(x => x.ActiveSelectionGeneration is null && string.Equals(x.LocalAccountKey, localAccountKey, StringComparison.Ordinal));
            if (previous is not null) return Result<PluginSessionCapture>.Ok(previous);
            var capture = _sessions.CaptureOwner(localAccountKey);
            if (capture.IsOk && !string.Equals(capture.Value!.LocalAccountKey, localAccountKey, StringComparison.Ordinal)) return Refused<PluginSessionCapture>();
            return Capture(capture);
        }
    }
    private Result<PluginSessionCapture> Capture(Result<PluginSessionCapture> result)
    {
        if (!result.IsOk || result.Value is null || !Grant.AccountKeys.Contains(result.Value.LocalAccountKey) || !_sessions.IsCurrent(result.Value)) return Refused<PluginSessionCapture>();
        _captures.Add(result.Value);
        _checked = false;
        return result;
    }
    public Result<Unit> Demand(string permission, string? localAccountKey)
    {
        lock (_gate) return IsLive() && Grant.Authorizes(Binding.Package, Binding.GrantGeneration, permission, localAccountKey)
            ? Result<Unit>.Ok(default) : Refused<Unit>();
    }
    public async ValueTask<bool> CheckCurrentAsync(CancellationToken ct)
    {
        lock (_gate) { _checked = false; if (!IsLive()) return false; }
        var current = await _authority.GetCurrentAsync(Binding.Package.Id, ct).ConfigureAwait(false);
        lock (_gate)
        {
            var value = current.Value;
            _checked = IsLive() && current.IsOk && value is not null && Matches(value, Binding)
                && value.Permissions.SetEquals(Grant.Permissions) && value.AccountKeys.SetEquals(Grant.AccountKeys)
                && _captures.TrueForAll(_sessions.IsCurrent);
            return _checked;
        }
    }
    public void MarkMutationIssued()
    {
        lock (_gate)
        {
            if (!_mayMutate || !_checked || !IsLive() || !_captures.TrueForAll(_sessions.IsCurrent))
                throw new InvalidOperationException("Plugin mutation requires a current authorized call context.");
            _issued = true;
            _checked = false;
        }
    }
    internal PluginDeliveryFence? SealForDelivery()
    {
        lock (_gate)
        {
            if (!_checked || !IsLive() || !_captures.TrueForAll(_sessions.IsCurrent))
            {
                return null;
            }

            _closed = true;
            _checked = false;
            return new PluginDeliveryFence(Binding, Grant, _sessions, [.. _captures], _issued);
        }
    }

    internal bool Close()
    {
        bool issued;
        lock (_gate) { _closed = true; _checked = false; issued = _issued; }
        _authority.Changed -= OnAuthorityChanged;
        return issued;
    }
    private void OnAuthorityChanged(object? sender, PluginAuthorityChangedEventArgs e)
    {
        if (!string.Equals(e.PluginId, Binding.Package.Id, StringComparison.Ordinal)) return;
        lock (_gate) { _closed = true; _checked = false; }
    }
    private bool IsLive() => !_closed && Binding.IsAlive && !_lifetime.IsCancellationRequested;
    internal static bool Matches(PluginGrantSnapshot value, PluginRuntimeBinding binding) => binding.IsAlive && value.Enabled
        && value.Package == binding.Package && value.Generation == binding.GrantGeneration && value.Tier == binding.Tier;
    private static Result<T> Refused<T>() => Result<T>.Fail(new ValidationError("Plugin account context is unavailable."));
}
