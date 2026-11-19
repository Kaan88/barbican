// Source: Bulwark lib/plugin-sandbox/host-api.ts:407-447 and storage-scope.ts:17-23; live binding/capture revalidation is an M6b addition.
using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Persistence;

/// <summary>Plugin user storage over a host-created runtime binding and captured local login.</summary>
public sealed class PluginScopedStorage : IPluginScopedStorage
{
    private const int Quota = 1024 * 1024;
    private readonly string _profile;
    private readonly IPluginAuthority _authority;
    private readonly IPluginSessionSource _sessions;
    private readonly IPluginUserStorageStore _store;

    public PluginScopedStorage(string profile, IPluginAuthority authority, IPluginSessionSource sessions, IPluginUserStorageStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(store);
        _profile = profile;
        _authority = authority;
        _sessions = sessions;
        _store = store;
    }

    private static Result<T> Fail<T>() => Result<T>.Fail(new ValidationError("Plugin storage is unavailable or the context is stale."));
    private async Task<bool> CurrentAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct)
    {
        if (!binding.IsAlive || !string.Equals(binding.Profile, _profile, StringComparison.Ordinal) || !_sessions.IsCurrent(capture)) return false;
        var grant = await _authority.GetCurrentAsync(binding.Package.Id, ct).ConfigureAwait(false);
        return grant.IsOk && grant.Value is not null && PluginInvocationContext.Matches(grant.Value, binding)
            && grant.Value.AccountKeys.Contains(capture.LocalAccountKey) && _sessions.IsCurrent(capture);
    }

    private async Task<Result<(PluginStoreSnapshot Snapshot, Dictionary<string, string> Values)>> ReadAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct)
    {
        if (!await CurrentAsync(binding, capture, ct).ConfigureAwait(false)) return Fail<(PluginStoreSnapshot, Dictionary<string, string>)>();
        var result = await _store.LoadAsync(_profile, binding.Package.Id, capture.LocalAccountKey, ct).ConfigureAwait(false);
        if (!result.IsOk) return Result<(PluginStoreSnapshot, Dictionary<string, string>)>.Fail(result.Error!);
        if (!await CurrentAsync(binding, capture, ct).ConfigureAwait(false)) return Fail<(PluginStoreSnapshot, Dictionary<string, string>)>();
        try { return Result<(PluginStoreSnapshot, Dictionary<string, string>)>.Ok((result.Value!, PluginStorageBinary.Decode(result.Value!.Index))); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or OverflowException) { return Fail<(PluginStoreSnapshot, Dictionary<string, string>)>(); }
    }

    public async Task<Result<PluginValue?>> GetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var loaded = await ReadAsync(binding, capture, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return Result<PluginValue?>.Fail(loaded.Error!);
        if (!loaded.Value.Values.TryGetValue(key, out var value)) return Result<PluginValue?>.Ok(null);
        try
        {
            using var doc = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 65 });
            var limits = new PluginValueLimits(MaxWireBytes: 4 * Quota, MaxStringBytes: 4 * Quota);
            return Result<PluginValue?>.Ok(PluginValue.FromJson(doc.RootElement, limits));
        }
        catch (Exception ex) when (ex is JsonException or PluginValueFormatException) { return Result<PluginValue?>.Ok(null); }
    }

    public async Task<Result<Unit>> SetAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, PluginValue value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (key.Length is 0 or > 256) return Fail<Unit>();
        string json;
        try
        {
            json = PluginStorageJson.Stringify(value);
            using var check = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 65 });
            _ = PluginValue.FromJson(check.RootElement, new PluginValueLimits(MaxWireBytes: 4 * Quota, MaxStringBytes: 4 * Quota));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or JsonException or PluginValueFormatException) { return Fail<Unit>(); }
        var loaded = await ReadAsync(binding, capture, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
        loaded.Value.Values[key] = json;
        return await WriteAsync(binding, capture, loaded.Value.Snapshot, loaded.Value.Values, ct).ConfigureAwait(false);
    }

    public async Task<Result<Unit>> RemoveAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var loaded = await ReadAsync(binding, capture, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
        if (!loaded.Value.Values.Remove(key)) return Result<Unit>.Ok(default);
        return await WriteAsync(binding, capture, loaded.Value.Snapshot, loaded.Value.Values, ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<string>>> KeysAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, CancellationToken ct)
    {
        var loaded = await ReadAsync(binding, capture, ct).ConfigureAwait(false);
        return loaded.IsOk ? Result<IReadOnlyList<string>>.Ok(loaded.Value.Values.Keys.ToArray()) : Result<IReadOnlyList<string>>.Fail(loaded.Error!);
    }

    private async Task<Result<Unit>> WriteAsync(PluginRuntimeBinding binding, PluginSessionCapture capture, PluginStoreSnapshot snapshot, Dictionary<string, string> values, CancellationToken ct)
    {
        try
        {
            var account = Uri.EscapeDataString(capture.LocalAccountKey).Replace("%21", "!", StringComparison.Ordinal).Replace("%27", "'", StringComparison.Ordinal).Replace("%28", "(", StringComparison.Ordinal).Replace("%29", ")", StringComparison.Ordinal).Replace("%2A", "*", StringComparison.Ordinal);
            var prefixLength = 10L + binding.Package.Id.Length + account.Length; // plugin:<id>:@<encoded account>:
            if (values.Sum(x => prefixLength + x.Key.Length + x.Value.Length) > Quota) return Fail<Unit>();
            var bytes = PluginStorageBinary.Encode(values);
            if (!await CurrentAsync(binding, capture, ct).ConfigureAwait(false)) return Fail<Unit>();
            var commit = await _store.CommitAsync(_profile, binding.Package.Id, capture.LocalAccountKey, snapshot.Revision, bytes, ct).ConfigureAwait(false);
            if (!commit.IsOk) return Result<Unit>.Fail(commit.Error!);
            return await CurrentAsync(binding, capture, ct).ConfigureAwait(false) ? Result<Unit>.Ok(default) : Fail<Unit>();
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidDataException or OverflowException) { return Fail<Unit>(); }
    }
}
