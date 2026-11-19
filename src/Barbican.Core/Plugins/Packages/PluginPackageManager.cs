// Source: Bulwark stores/plugin-store.ts:188-320, 467-516; M6b adds exact review/generation and authenticated persistence boundaries.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages.Inspection;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Persistence;

namespace Barbican.Core.Plugins.Packages;

/// <summary>Package lifecycle with expiring reviews and authenticated host persistence; never loads executable code.</summary>
public sealed partial class PluginPackageManager : IPluginPackageManager, IPluginAuthority, IDisposable
{
    private readonly PluginPackageManagerOptions _options;
    private readonly IPluginPackageStore _store;
    private readonly IPluginRevocationCoordinator _revocation;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Review> _reviews = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);
    private sealed record Review(InspectedPluginPackage Inspection, byte[] Archive, PluginPackageDefinition Definition, PluginPackageIdentity? Previous, DateTimeOffset Expires);
    internal sealed record Entry(string Id, string Version, string Hash, bool Enabled, long Generation, string[] Permissions, string[] Accounts, long? CleanupThrough = null);
    internal sealed record Index(long NextGeneration, List<Entry> Entries);
    private sealed record Loaded(long Revision, Index Index, Dictionary<string, PluginInstallation> Installations);

    public PluginPackageManager(PluginPackageManagerOptions options, IPluginPackageStore store, IPluginRevocationCoordinator revocation, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Profile)) throw new ArgumentException("A host profile is required.", nameof(options));
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentNullException.ThrowIfNull(time);
        if (options.ReviewLifetime <= TimeSpan.Zero || options.MaxPendingReviews <= 0 || options.MaxExpandedStagingBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        _options = options;
        _store = store;
        _revocation = revocation;
        _time = time;
    }

    [JsonSerializable(typeof(Index))]
    internal sealed partial class PackageIndexJsonContext : JsonSerializerContext;

    public void Dispose() => _gate.Dispose();

    public event EventHandler<PluginAuthorityChangedEventArgs>? Changed;
    private static Result<T> Failure<T>(string message) => Result<T>.Fail(new ValidationError(message));
    private static PluginPackageIdentity Identity(Entry entry) => new(entry.Id, entry.Version, entry.Hash);
    private static PluginPackageDefinition Definition(InspectedPluginPackage inspected)
    {
        using var manifest = JsonDocument.Parse(inspected.ManifestJson);
        return new PluginPackageDefinition(new(inspected.Id, inspected.Version, inspected.WholeArchiveSha256), PluginRuntimeKind.JavaScript, inspected.EntryPoint, manifest.RootElement, inspected.Permissions, PluginPackageSource.LocalImport);
    }
    private static PluginGrantSnapshot Grant(Entry e) => new(Identity(e), e.Generation, e.Enabled, e.Permissions, e.Accounts);

    private async Task<Result<Loaded>> LoadAsync(CancellationToken ct)
    {
        var stored = await _store.LoadAsync(_options.Profile, ct).ConfigureAwait(false);
        if (!stored.IsOk) return Result<Loaded>.Fail(stored.Error!);
        try
        {
            var snapshot = stored.Value!;
            if (snapshot.Index.Length > 4 * 1024 * 1024) return Failure<Loaded>("Package index exceeds the host limit.");
            var index = snapshot.Index.IsEmpty ? new Index(1, []) : JsonSerializer.Deserialize(snapshot.Index.AsSpan(), PackageIndexJsonContext.Default.Index);
            if (index is null || index.NextGeneration <= 0 || index.Entries is null || index.Entries.Count > 1000) return Failure<Loaded>("Invalid package index.");
            var installations = new Dictionary<string, PluginInstallation>(StringComparer.Ordinal);
            foreach (var entry in index.Entries)
            {
                if (entry is null || entry.Permissions is null || entry.Accounts is null || entry.Generation <= 0 || entry.Generation >= index.NextGeneration || (entry.CleanupThrough is not null && (entry.CleanupThrough <= 0 || entry.Enabled))) return Failure<Loaded>("Invalid package grant.");
                var identity = Identity(entry);
                var bytes = await _store.ReadContentAsync(_options.Profile, identity, ct).ConfigureAwait(false);
                if (!bytes.IsOk || bytes.Value.IsDefault || bytes.Value.Length > 5 * 1024 * 1024) return Failure<Loaded>("Package content is unavailable.");
                using var input = new MemoryStream(bytes.Value.ToArray(), writable: false);
                var inspected = await PluginPackageInspector.InspectAsync(input, _options.BulwarkCompatibilityVersion, ct).ConfigureAwait(false);
                if (!inspected.IsOk || Definition(inspected.Value!).Identity != identity) return Failure<Loaded>("Package content does not match its authenticated identity.");
                var definition = Definition(inspected.Value!);
                if (!definition.RequestedPermissions.SetEquals(entry.Permissions) || entry.Accounts.Any(string.IsNullOrWhiteSpace)) return Failure<Loaded>("Invalid package grant.");
                if (!installations.TryAdd(entry.Id, new(definition, entry.Enabled && !_blocked.Contains(entry.Id), Grant(entry)))) return Failure<Loaded>("Duplicate package identity.");
            }
            return Result<Loaded>.Ok(new(snapshot.Revision, index, installations));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return Failure<Loaded>("Invalid authenticated package index.");
        }
    }

    private async Task<Result<Unit>> CommitAsync(Loaded previous, Index next, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(next, PackageIndexJsonContext.Default.Index);
        if (data.Length > 4 * 1024 * 1024) return Failure<Unit>("Package index exceeds the host limit.");
        var committed = await _store.CommitAsync(_options.Profile, previous.Revision, data, ct).ConfigureAwait(false);
        if (committed.IsOk) return Result<Unit>.Ok(default);
        // An uncertain acknowledgement is only successful after an authenticated exact readback.
        var readback = await _store.LoadAsync(_options.Profile, ct).ConfigureAwait(false);
        return readback.IsOk && readback.Value!.Revision > previous.Revision && readback.Value.Index.AsSpan().SequenceEqual(data)
            ? Result<Unit>.Ok(default) : Result<Unit>.Fail(committed.Error!);
    }

    public async Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, PluginPackageSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (source != PluginPackageSource.LocalImport) return Failure<PluginInstallPreview>("The source has no verified host provenance.");
        byte[] bytes;
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await archive.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 5 * 1024 * 1024) return Failure<PluginInstallPreview>("Package exceeds the host limit.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            bytes = buffer.ToArray();
        }
        catch (IOException) { return Failure<PluginInstallPreview>("Unable to read package."); }
        using var input = new MemoryStream(bytes, writable: false);
        var inspected = await PluginPackageInspector.InspectAsync(input, _options.BulwarkCompatibilityVersion, ct).ConfigureAwait(false);
        if (!inspected.IsOk) return Result<PluginInstallPreview>.Fail(inspected.Error!);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<PluginInstallPreview>.Fail(loaded.Error!);
            var definition = Definition(inspected.Value!);
            loaded.Value!.Installations.TryGetValue(definition.Identity.Id, out var previous);
            return Stage(inspected.Value!, bytes, definition, previous);
        }
        finally { _gate.Release(); }
    }

    private Result<PluginInstallPreview> Stage(InspectedPluginPackage inspected, byte[] bytes, PluginPackageDefinition definition, PluginInstallation? previous)
    {
        foreach (var expired in _reviews.Where(x => x.Value.Expires <= _time.GetUtcNow()).Select(x => x.Key).ToArray()) _reviews.Remove(expired);
        var size = inspected.Resources.Sum(x => (long)x.Value.Length);
        var used = _reviews.Values.Sum(x => x.Inspection.Resources.Sum(y => (long)y.Value.Length));
        if (_reviews.Count >= _options.MaxPendingReviews || size > _options.MaxExpandedStagingBytes - used) return Failure<PluginInstallPreview>("Review staging is full.");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _reviews.Add(token, new(inspected, bytes, definition, previous?.Package.Identity, _time.GetUtcNow() + _options.ReviewLifetime));
        return Result<PluginInstallPreview>.Ok(new(token, definition, previous));
    }

    public async Task<Result<PluginInstallPreview>> ReviewAsync(PluginPackageIdentity installed, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<PluginInstallPreview>.Fail(loaded.Error!);
            if (!loaded.Value!.Installations.TryGetValue(installed.Id, out var current) || current.Package.Identity != installed) return Failure<PluginInstallPreview>("Installed package changed.");
            var content = await _store.ReadContentAsync(_options.Profile, installed, ct).ConfigureAwait(false);
            if (!content.IsOk) return Result<PluginInstallPreview>.Fail(content.Error!);
            var bytes = content.Value.ToArray();
            using var input = new MemoryStream(bytes, writable: false);
            var inspected = await PluginPackageInspector.InspectAsync(input, _options.BulwarkCompatibilityVersion, ct).ConfigureAwait(false);
            return inspected.IsOk ? Stage(inspected.Value!, bytes, current.Package, current) : Result<PluginInstallPreview>.Fail(inspected.Error!);
        }
        finally { _gate.Release(); }
    }

    public async Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_reviews.TryGetValue(decision.ReviewId, out var review) || review.Expires <= _time.GetUtcNow() || review.Definition.Identity != decision.Candidate) return Failure<PluginInstallation>("Review is invalid or expired.");
            if (!review.Definition.RequestedPermissions.SetEquals(decision.ApprovedPermissions) || decision.AccountKeys.Any(string.IsNullOrWhiteSpace)) return Failure<PluginInstallation>("The approval does not match the requested permissions.");
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<PluginInstallation>.Fail(loaded.Error!);
            loaded.Value!.Installations.TryGetValue(review.Definition.Identity.Id, out var previous);
            if (previous?.Package.Identity != review.Previous)
            {
                _reviews.Remove(decision.ReviewId);
                return Failure<PluginInstallation>("The installed package changed since review.");
            }
            if (previous is not null)
            {
                var stopped = await StopAsync(loaded.Value, previous.Package.Identity, ct).ConfigureAwait(false);
                if (!stopped.IsOk) return Result<PluginInstallation>.Fail(stopped.Error!);
                loaded = await LoadAsync(ct).ConfigureAwait(false);
                if (!loaded.IsOk) return Result<PluginInstallation>.Fail(loaded.Error!);
            }
            var put = await _store.PutContentAsync(_options.Profile, review.Definition.Identity, review.Archive, ct).ConfigureAwait(false);
            if (!put.IsOk) return Result<PluginInstallation>.Fail(put.Error!);
            var oldIndex = loaded.Value!.Index;
            var entry = new Entry(review.Definition.Identity.Id, review.Definition.Identity.Version, review.Definition.Identity.Sha256, false, oldIndex.NextGeneration, decision.ApprovedPermissions.ToArray(), decision.AccountKeys.ToArray());
            var next = new Index(checked(oldIndex.NextGeneration + 1), [.. oldIndex.Entries.Where(x => !string.Equals(x.Id, entry.Id, StringComparison.Ordinal)), entry]);
            var commit = await CommitAsync(loaded.Value, next, ct).ConfigureAwait(false);
            if (!commit.IsOk) return Result<PluginInstallation>.Fail(commit.Error!);
            _reviews.Remove(decision.ReviewId);
            _blocked.Remove(entry.Id);
            Changed?.Invoke(this, new(entry.Id, entry.Generation));
            return Result<PluginInstallation>.Ok(new(review.Definition, false, Grant(entry)));
        }
        finally { _gate.Release(); }
    }

    private async Task<Result<Unit>> StopAsync(Loaded loaded, PluginPackageIdentity identity, CancellationToken ct)
    {
        var entry = loaded.Index.Entries.SingleOrDefault(x => Identity(x) == identity);
        if (entry is null) return Failure<Unit>("Installed package changed.");
        _blocked.Add(identity.Id);
        var through = entry.CleanupThrough ?? entry.Generation;
        if (entry.CleanupThrough is null)
        {
            var stopped = entry with { Enabled = false, Generation = loaded.Index.NextGeneration, CleanupThrough = through };
            var index = new Index(checked(loaded.Index.NextGeneration + 1), [.. loaded.Index.Entries.Select(x => x == entry ? stopped : x)]);
            var saved = await CommitAsync(loaded, index, ct).ConfigureAwait(false);
            if (!saved.IsOk) return saved;
            Changed?.Invoke(this, new(identity.Id, stopped.Generation));
        }
        var cleanup = await _revocation.RevokeAsync(identity, through, ct).ConfigureAwait(false);
        if (!cleanup.IsOk) return cleanup;
        var latest = await LoadAsync(ct).ConfigureAwait(false);
        if (!latest.IsOk) return Result<Unit>.Fail(latest.Error!);
        var pending = latest.Value!.Index.Entries.SingleOrDefault(x => Identity(x) == identity && x.CleanupThrough == through);
        if (pending is null) return Failure<Unit>("Package changed during cleanup.");
        var cleared = latest.Value.Index with { Entries = [.. latest.Value.Index.Entries.Select(x => x == pending ? pending with { CleanupThrough = null } : x)] };
        var committed = await CommitAsync(latest.Value, cleared, ct).ConfigureAwait(false);
        if (!committed.IsOk) return committed;
        _blocked.Remove(identity.Id);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            return loaded.IsOk ? Result<IReadOnlyList<PluginInstallation>>.Ok(loaded.Value!.Installations.Values.ToArray()) : Result<IReadOnlyList<PluginInstallation>>.Fail(loaded.Error!);
        }
        finally { _gate.Release(); }
    }

    public async Task<Result<PluginInstallation?>> FindAsync(string pluginId, CancellationToken ct)
    {
        var list = await ListAsync(ct).ConfigureAwait(false);
        return list.IsOk ? Result<PluginInstallation?>.Ok(list.Value!.SingleOrDefault(x => string.Equals(x.Package.Identity.Id, pluginId, StringComparison.Ordinal))) : Result<PluginInstallation?>.Fail(list.Error!);
    }

    public async Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct)
    {
        var item = await FindAsync(pluginId, ct).ConfigureAwait(false);
        return item.IsOk ? Result<PluginGrantSnapshot?>.Ok(item.Value?.Enabled == true ? item.Value.Grant : null) : Result<PluginGrantSnapshot?>.Fail(item.Error!);
    }

    public async Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            var entry = loaded.Value!.Index.Entries.SingleOrDefault(x => Identity(x) == package);
            if (entry is null) return Failure<Unit>("Installed package changed.");
            if (!enabled) return await StopAsync(loaded.Value, package, ct).ConfigureAwait(false);
            if (_blocked.Contains(package.Id) || entry.CleanupThrough is not null) return Failure<Unit>("Package cleanup is pending.");
            if (entry.Enabled) return Result<Unit>.Ok(default);
            var updated = entry with { Enabled = true, Generation = loaded.Value.Index.NextGeneration };
            var index = new Index(checked(loaded.Value.Index.NextGeneration + 1), [.. loaded.Value.Index.Entries.Select(x => x == entry ? updated : x)]);
            var result = await CommitAsync(loaded.Value, index, ct).ConfigureAwait(false);
            if (result.IsOk) Changed?.Invoke(this, new(package.Id, updated.Generation));
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            var stopped = await StopAsync(loaded.Value!, package, ct).ConfigureAwait(false);
            if (!stopped.IsOk) return stopped;
            loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            var index = loaded.Value!.Index with { Entries = [.. loaded.Value.Index.Entries.Where(x => Identity(x) != package)] };
            var saved = await CommitAsync(loaded.Value, index, ct).ConfigureAwait(false);
            if (!saved.IsOk) return saved;
            Changed?.Invoke(this, new(package.Id, index.NextGeneration));
            return await _store.DeleteContentAsync(_options.Profile, package, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<Result<Stream>> OpenResourceAsync(PluginPackageIdentity package, string resource, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Stream>.Fail(loaded.Error!);
            if (!loaded.Value!.Installations.TryGetValue(package.Id, out var installation) || installation.Package.Identity != package) return Failure<Stream>("Installed package changed.");
            var content = await _store.ReadContentAsync(_options.Profile, package, ct).ConfigureAwait(false);
            if (!content.IsOk) return Result<Stream>.Fail(content.Error!);
            using var archive = new MemoryStream(content.Value.ToArray(), writable: false);
            var inspected = await PluginPackageInspector.InspectAsync(archive, _options.BulwarkCompatibilityVersion, ct).ConfigureAwait(false);
            if (!inspected.IsOk || Definition(inspected.Value!).Identity != package || !inspected.Value!.Resources.TryGetValue(resource, out var bytes)) return Failure<Stream>("Package resource is unavailable.");
            return Result<Stream>.Ok(new MemoryStream(bytes.ToArray(), writable: false));
        }
        finally { _gate.Release(); }
    }
}
