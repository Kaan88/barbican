// Source: Bulwark lib/plugin-sandbox/host-api.ts:407-447 and storage-scope.ts:17-23; live binding/capture revalidation is an M6b addition.
using System.Runtime.CompilerServices;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Plugin user storage over a host-created runtime binding and captured local login.</summary>
public sealed class PluginScopedStorage : IPluginScopedStorage
{
    private readonly string _profile;
    private readonly IPluginDeliveryAuthority _authority;
    private readonly IPluginSessionSource _sessions;
    private readonly IPluginUserStorageStore _store;
    private static readonly ConditionalWeakTable<IPluginUserStorageStore, SemaphoreSlim[]> WriteGates = new();
    private readonly SemaphoreSlim[] _writes;

    public PluginScopedStorage(string profile, IPluginDeliveryAuthority authority, IPluginSessionSource sessions, IPluginUserStorageStore store)
    {
        if (!ProfilePaths.IsValidName(profile)) throw new ArgumentException("Invalid host profile.", nameof(profile));
        _profile = profile;
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _writes = WriteGates.GetValue(store, static _ => [.. Enumerable.Range(0, 16).Select(_ => new SemaphoreSlim(1, 1))]);
    }

    public async Task<Result<PluginValue?>> GetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var loaded = await LoadAsync(binding, capture, ct).ConfigureAwait(false);
        if (loaded is null) return Unavailable<PluginValue?>();
        var raw = loaded.Values.GetValueOrDefault(key);
        var value = raw is null ? null : PluginStorageSnapshot.ParseValue(raw);
        if (raw is not null && value is null) return Unavailable<PluginValue?>();
        return await IsCurrentAsync(binding, capture, ct).ConfigureAwait(false)
            ? Result<PluginValue?>.Ok(value)
            : Unavailable<PluginValue?>();
    }

    public async Task<Result<IReadOnlyList<string>>> KeysAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct)
    {
        var loaded = await LoadAsync(binding, capture, ct).ConfigureAwait(false);
        if (loaded is null) return Unavailable<IReadOnlyList<string>>();
        var keys = loaded.Values.Keys.ToArray();
        return await IsCurrentAsync(binding, capture, ct).ConfigureAwait(false)
            ? Result<IReadOnlyList<string>>.Ok(keys)
            : Unavailable<IReadOnlyList<string>>();
    }

    public Task<Result<Unit>> SetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, PluginValue value, CancellationToken ct) =>
        SetAsync(binding, capture, key, value, null, ct);

    internal Task<Result<Unit>> SetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, PluginValue value, Action? issued, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (key is not { Length: > 0 and <= 256 })
            return Task.FromResult(Result<Unit>.Fail(new ValidationError("Invalid add-on storage key.")));
        return ChangeAsync(binding, capture, key, value, issued, ct);
    }

    public Task<Result<Unit>> RemoveAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct) =>
        RemoveAsync(binding, capture, key, null, ct);

    internal Task<Result<Unit>> RemoveAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, Action? issued, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ChangeAsync(binding, capture, key, null, issued, ct);
    }

    private async Task<Result<Unit>> ChangeAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key,
        PluginValue? value, Action? issued, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(capture);
        var hash = HashCode.Combine(binding.Package.Id, capture.LocalAccountKey);
        var gate = _writes[(int)((uint)hash % _writes.Length)];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ChangeUnderGateAsync(binding, capture, key, value, issued, ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task<Result<Unit>> ChangeUnderGateAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key,
        PluginValue? value, Action? issued, CancellationToken ct)
    {
        var loaded = await LoadAsync(binding, capture, ct).ConfigureAwait(false);
        if (loaded is null) return Unavailable<Unit>();
        if (value is null)
        {
            if (!loaded.Values.Remove(key))
                return await IsCurrentAsync(binding, capture, ct).ConfigureAwait(false) ? Result<Unit>.Ok(default) : Unavailable<Unit>();
        }
        else if (!loaded.TrySet(key, value))
        {
            return Result<Unit>.Fail(new ValidationError("The add-on value exceeds storage limits or cannot be stored as JSON."));
        }

        var data = loaded.Encode();
        var committed = await CommitAsync(binding, capture, loaded.Revision, data, issued, ct).ConfigureAwait(false);
        return committed && await IsCurrentAsync(binding, capture, ct).ConfigureAwait(false)
            ? Result<Unit>.Ok(default)
            : Unavailable<Unit>();
    }

    private async Task<bool> CommitAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, long revision, byte[] data,
        Action? issued, CancellationToken ct)
    {
        Task<Result<long>>? commit = null;
        var owner = new PluginSessionCapture(capture.Session, null);
        var admitted = await _authority.TryPostAsync(binding, grant =>
            Matches(grant, binding, capture) && _sessions.TryRunWhileCurrent([capture], () =>
                !ct.IsCancellationRequested && binding.TryWriteWhileAlive(() =>
                {
                    issued?.Invoke();
                    commit = _store.CommitAsync(_profile, binding.Package.Id, capture.LocalAccountKey, revision, data,
                        publish => Publish(binding, owner, publish, ct), ct);
                })), ct).ConfigureAwait(false);
        if (!admitted || commit is null) return false;
        var result = await commit.ConfigureAwait(false);
        return result.IsOk;
    }

    private bool Publish(PluginRuntimeBinding binding, PluginSessionCapture owner, Func<bool> write, CancellationToken ct) =>
        _authority.TryContinuePost(binding, () => _sessions.TryRunWhileCurrent([owner], () =>
        {
            var published = false;
            return !ct.IsCancellationRequested && binding.TryWriteWhileAlive(() => published = write()) && published;
        }));

    private async Task<PluginStorageSnapshot?> LoadAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(capture);
        if (!capture.Session.AllowsAddOnStorage || !await IsCurrentAsync(binding, capture, ct).ConfigureAwait(false)) return null;
        var current = await _store.LoadAsync(_profile, binding.Package.Id, capture.LocalAccountKey, ct).ConfigureAwait(false);
        return current.IsOk && current.Value is { } snapshot
            ? PluginStorageSnapshot.Decode(snapshot, binding.Package.Id, capture.LocalAccountKey)
            : null;
    }

    private Task<bool> IsCurrentAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct) =>
        !string.Equals(binding.Profile, _profile, StringComparison.Ordinal) || !binding.IsAlive
            ? Task.FromResult(false)
            : _authority.TryPostAsync(binding, grant => Matches(grant, binding, capture) &&
                _sessions.TryRunWhileCurrent([capture], () => !ct.IsCancellationRequested && binding.IsAlive), ct);

    private static bool Matches(PluginGrantSnapshot grant, PluginRuntimeBinding binding, PluginSessionCapture capture) =>
        PluginInvocationContext.Matches(grant, binding) && grant.AccountKeys.Contains(capture.LocalAccountKey);

    private static Result<T> Unavailable<T>() => Result<T>.Fail(new ValidationError("Add-on storage is unavailable."));
}
