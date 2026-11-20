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
    private const int MaxInstalledPackages = 1000;

    private readonly PluginPackageManagerOptions _options;
    private readonly IPluginPackageStore _store;
    private readonly IPluginRevocationCoordinator _revocation;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPluginProfileAuthorityLease? _lease;
    private readonly CancellationTokenRegistration _leaseRegistration;
    private readonly Lock _issuanceGate = new();
    private readonly Dictionary<PluginPackageIdentity, long> _issued = [];
    private int _authorityClosed;
    private int _disposed;
    private Loaded? _cache;
    private readonly Dictionary<string, Review> _reviews = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);
    private sealed record Review(InspectedPluginPackage Inspection, byte[] Archive, PluginPackageDefinition Definition, PluginPackageIdentity? Previous, long? PreviousGeneration, DateTimeOffset Expires);
    internal sealed record Entry(string Id, string Version, string Hash, bool Enabled, long Generation, string[] Permissions, string[] Accounts, long? CleanupThrough = null);
    internal sealed record Index(long NextGeneration, List<Entry> Entries, List<PluginPackageIdentity>? RetiredArchives = null);
    private sealed record Loaded(long Revision, Index Index, Dictionary<string, PluginInstallation> Installations);

    internal PluginPackageManager(PluginPackageManagerOptions options, IPluginPackageStore store, IPluginRevocationCoordinator revocation, TimeProvider time)
        : this(options, store, revocation, time, null, testOnly: true) { }

    public PluginPackageManager(PluginPackageManagerOptions options, IPluginPackageStore store, IPluginRevocationCoordinator revocation, TimeProvider time, IPluginProfileAuthorityLease lease)
        : this(options, store, revocation, time, lease ?? throw new ArgumentNullException(nameof(lease)), testOnly: false) { }

    private PluginPackageManager(PluginPackageManagerOptions options, IPluginPackageStore store, IPluginRevocationCoordinator revocation, TimeProvider time, IPluginProfileAuthorityLease? lease, bool testOnly)
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
        if (!testOnly && (lease is null || !lease.IsHeld || lease.Lifetime.IsCancellationRequested || !string.Equals(lease.Profile, options.Profile, StringComparison.Ordinal))) throw new ArgumentException("A held matching profile lease is required.", nameof(lease));
        _lease = lease;
        _leaseRegistration = lease?.Lifetime.Register(OnLeaseLost) ?? default;
    }

    [JsonSerializable(typeof(Index))]
    internal sealed partial class PackageIndexJsonContext : JsonSerializerContext;

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        OnLeaseLost();
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _leaseRegistration.Dispose();
        // The managed async gate remains available to in-flight calls while they unwind.
        // No caller accesses SemaphoreSlim.AvailableWaitHandle.
    }

    private bool HasAuthority => Volatile.Read(ref _authorityClosed) == 0
        && (_lease is null || (_lease.IsHeld && !_lease.Lifetime.IsCancellationRequested));

    private void OnLeaseLost()
    {
        KeyValuePair<PluginPackageIdentity, long>[] issued;
        lock (_issuanceGate)
        {
            Volatile.Write(ref _authorityClosed, 1);
            issued = [.. _issued];
        }

        // Both shutdown paths establish the same idempotent barrier. A lease callback
        // racing Dispose must finish its own barrier before the lease handle is released.
        foreach (var (identity, generation) in issued)
        {
            try { _ = _revocation.RevokeAsync(identity, generation, CancellationToken.None); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        }
    }

    public event EventHandler<PluginAuthorityChangedEventArgs>? Changed;
    private static Result<T> Failure<T>(string message) => Result<T>.Fail(new ValidationError(message));
    private static PluginPackageIdentity Identity(Entry entry) => new(entry.Id, entry.Version, entry.Hash);
    private static PluginPackageDefinition Definition(InspectedPluginPackage inspected)
    {
        using var manifest = JsonDocument.Parse(inspected.ManifestJson);
        var tier = manifest.RootElement.TryGetProperty("tier", out var requested) && string.Equals(requested.GetString(), "privileged", StringComparison.Ordinal) ? PluginExecutionTier.Privileged : PluginExecutionTier.Untrusted;
        return new PluginPackageDefinition(new(inspected.Id, inspected.Version, inspected.WholeArchiveSha256), PluginRuntimeKind.JavaScript, inspected.EntryPoint, manifest.RootElement, inspected.Permissions, PluginPackageSource.LocalImport, tier);
    }
    private static PluginGrantSnapshot Grant(Entry e) => new(Identity(e), e.Generation, e.Enabled, e.Permissions, e.Accounts);

    private async Task<Result<Loaded>> LoadAsync(CancellationToken ct)
    {
        if (!HasAuthority) return Failure<Loaded>("Plugin profile authority is unavailable.");
        if (_lease is not null && _cache is not null) return Result<Loaded>.Ok(_cache);
        var stored = await _store.LoadAsync(_options.Profile, ct).ConfigureAwait(false);
        if (!HasAuthority) return Failure<Loaded>("Plugin profile authority is unavailable.");
        if (!stored.IsOk) return Result<Loaded>.Fail(stored.Error!);
        try
        {
            var snapshot = stored.Value!;
            if (snapshot.Index.Length > 4 * 1024 * 1024) return Failure<Loaded>("Package index exceeds the host limit.");
            var index = snapshot.Index.IsEmpty ? new Index(1, []) : JsonSerializer.Deserialize(snapshot.Index.AsSpan(), PackageIndexJsonContext.Default.Index);
            if (index is null || !ValidIndex(index)) return Failure<Loaded>("Invalid package index.");
            var installations = new Dictionary<string, PluginInstallation>(StringComparer.Ordinal);
            foreach (var entry in index.Entries)
            {
                if (entry.Permissions is null || entry.Accounts is null || entry.Generation <= 0 || entry.Generation >= index.NextGeneration
                    || (entry.CleanupThrough is { } through && (through <= 0 || through >= entry.Generation || entry.Enabled)))
                    return Failure<Loaded>("Invalid package grant.");
                var identity = Identity(entry);
                var bytes = await _store.ReadContentAsync(_options.Profile, identity, ct).ConfigureAwait(false);
                if (!bytes.IsOk || bytes.Value.IsDefault || bytes.Value.Length > 5 * 1024 * 1024) return Failure<Loaded>("Package content is unavailable.");
                using var input = new MemoryStream(bytes.Value.ToArray(), writable: false);
                var inspected = await PluginPackageInspector.InspectAsync(input, _options.BulwarkCompatibilityVersion, ct).ConfigureAwait(false);
                if (!inspected.IsOk || Definition(inspected.Value!).Identity != identity) return Failure<Loaded>("Package content does not match its authenticated identity.");
                var definition = Definition(inspected.Value!);
                if (definition.RequestedTier != PluginExecutionTier.Untrusted || !definition.RequestedPermissions.SetEquals(entry.Permissions) || entry.Accounts.Any(string.IsNullOrWhiteSpace)) return Failure<Loaded>("Invalid package grant.");
                if (!installations.TryAdd(entry.Id, new(definition, entry.Enabled && !_blocked.Contains(entry.Id), Grant(entry)))) return Failure<Loaded>("Duplicate package identity.");
            }
            if (!HasAuthority) return Failure<Loaded>("Plugin profile authority is unavailable.");
            return Publish(snapshot.Revision, index, installations);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return Failure<Loaded>("Invalid authenticated package index.");
        }
    }

    private static bool ValidIndex(Index index)
    {
        if (index.Entries is null || index.Entries.Count > MaxInstalledPackages
            || index.Entries.Any(entry => entry is null) || !HasGenerationCapacity(index))
            return false;

        var hashes = index.Entries.Select(entry => Identity(entry).Sha256).ToHashSet(StringComparer.Ordinal);
        return index.RetiredArchives is null
            || index.RetiredArchives.All(archive => archive is not null && hashes.Add(archive.Sha256));
    }

    // Reserve one generation for each enabled package so all can still be disabled.
    private static bool HasGenerationCapacity(Index index) =>
        index.NextGeneration > 0 && index.NextGeneration <= long.MaxValue - 1 - index.Entries.Count(entry => entry.Enabled);

    private Result<Loaded> Publish(long revision, Index index, Dictionary<string, PluginInstallation> installations)
    {
        lock (_issuanceGate)
        {
            if (!HasAuthority) return Failure<Loaded>("Plugin profile authority is unavailable.");

            var loaded = new Loaded(revision, index, installations);
            foreach (var entry in index.Entries)
            {
                var identity = Identity(entry);
                _issued[identity] = Math.Max(_issued.GetValueOrDefault(identity), entry.Generation);
            }

            if (_lease is not null) _cache = loaded;
            return Result<Loaded>.Ok(loaded);
        }
    }

    private async Task<Result<Unit>> CommitAsync(Loaded previous, Index next, CancellationToken ct)
    {
        _cache = null;
        if (!HasAuthority) return Failure<Unit>("Plugin profile authority is unavailable.");
        if (!HasGenerationCapacity(next)) return Failure<Unit>("The package generation limit has been reached.");
        var data = JsonSerializer.SerializeToUtf8Bytes(next, PackageIndexJsonContext.Default.Index);
        if (data.Length > 4 * 1024 * 1024) return Failure<Unit>("Package index exceeds the host limit.");
        var committed = await _store.CommitAsync(_options.Profile, previous.Revision, data, ct).ConfigureAwait(false);
        if (!HasAuthority) return Failure<Unit>("Plugin profile authority is unavailable.");
        if (committed.IsOk) return Result<Unit>.Ok(default);
        // An uncertain acknowledgement is only successful after an authenticated exact readback.
        var readback = await _store.LoadAsync(_options.Profile, ct).ConfigureAwait(false);
        return HasAuthority && readback.IsOk && readback.Value!.Revision > previous.Revision && readback.Value.Index.AsSpan().SequenceEqual(data)
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
        _reviews.Add(token, new(inspected, bytes, definition, previous?.Package.Identity, previous?.Grant?.Generation, _time.GetUtcNow() + _options.ReviewLifetime));
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
            if (!HasAuthority) return Failure<PluginInstallPreview>("Plugin profile authority is unavailable.");
            return inspected.IsOk ? Stage(inspected.Value!, bytes, current.Package, current) : Result<PluginInstallPreview>.Fail(inspected.Error!);
        }
        finally { _gate.Release(); }
    }

    public async Task DiscardReviewAsync(string reviewId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { _reviews.Remove(reviewId); }
        finally { _gate.Release(); }
    }

    public async Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var notifications = new List<PluginAuthorityChangedEventArgs>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_reviews.TryGetValue(decision.ReviewId, out var review) || review.Expires <= _time.GetUtcNow() || review.Definition.Identity != decision.Candidate) return Failure<PluginInstallation>("Review is invalid or expired.");
            if (!review.Definition.RequestedPermissions.SetEquals(decision.ApprovedPermissions) || decision.AccountKeys.Any(string.IsNullOrWhiteSpace)) return Failure<PluginInstallation>("The approval does not match the requested permissions.");
            if (review.Definition.RequestedTier != PluginExecutionTier.Untrusted) return Failure<PluginInstallation>("Privileged packages require verified managed signature and consent.");
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<PluginInstallation>.Fail(loaded.Error!);
            loaded.Value!.Installations.TryGetValue(review.Definition.Identity.Id, out var previous);
            if (previous?.Package.Identity != review.Previous || previous?.Grant?.Generation != review.PreviousGeneration)
            {
                _reviews.Remove(decision.ReviewId);
                return Failure<PluginInstallation>("The installed package changed since review.");
            }
            if (previous is null && loaded.Value.Index.Entries.Count >= MaxInstalledPackages)
            {
                return Failure<PluginInstallation>("The profile has reached its installed package limit.");
            }

            if (previous is not null)
            {
                var stopped = await StopAsync(loaded.Value, previous.Package.Identity, notifications, ct).ConfigureAwait(false);
                if (!stopped.IsOk) return Result<PluginInstallation>.Fail(stopped.Error!);
                loaded = await LoadAsync(ct).ConfigureAwait(false);
                if (!loaded.IsOk) return Result<PluginInstallation>.Fail(loaded.Error!);
            }
            var saved = await SaveInstallationAsync(loaded.Value!, review, decision, ct).ConfigureAwait(false);
            if (!saved.IsOk) return Result<PluginInstallation>.Fail(saved.Error!);
            var entry = saved.Value!;
            _reviews.Remove(decision.ReviewId);
            _blocked.Remove(entry.Id);
            notifications.Add(new(entry.Id, entry.Generation));
            return Result<PluginInstallation>.Ok(new(review.Definition, false, Grant(entry)));
        }
        finally
        {
            _gate.Release();
            foreach (var notification in notifications) Changed?.Invoke(this, notification);
        }
    }

    private async Task<Result<Entry>> SaveInstallationAsync(Loaded loaded, Review review, PluginInstallDecision decision, CancellationToken ct)
    {
        var identity = review.Definition.Identity;
        var current = loaded.Index;
        var entry = new Entry(identity.Id, identity.Version, identity.Sha256, false, current.NextGeneration,
            decision.ApprovedPermissions.ToArray(), decision.AccountKeys.ToArray());
        List<Entry> entries = [.. current.Entries.Where(x => !string.Equals(x.Id, identity.Id, StringComparison.Ordinal)), entry];
        var next = WithRetiredArchive(current, entries, review.Previous) with { NextGeneration = current.NextGeneration + 1 };
        if (!HasGenerationCapacity(next)) return Failure<Entry>("The package generation limit has been reached.");

        var staged = await StageArchiveAsync(loaded, identity, ct).ConfigureAwait(false);
        if (!staged.IsOk) return Result<Entry>.Fail(staged.Error!);
        loaded = staged.Value!;
        var put = await _store.PutContentAsync(_options.Profile, identity, review.Archive, ct).ConfigureAwait(false);
        if (!put.IsOk) return Result<Entry>.Fail(put.Error!);

        var committed = await CommitAsync(loaded, next, ct).ConfigureAwait(false);
        if (!committed.IsOk) return Result<Entry>.Fail(committed.Error!);

        // The replacement is already durable. Failed deletion stays in the index for retry.
        if (review.Previous is { } previous && next.RetiredArchives!.Contains(previous))
        {
            var latest = await LoadAsync(CancellationToken.None).ConfigureAwait(false);
            if (latest.IsOk) _ = await DeleteRetiredArchiveAsync(latest.Value!, previous, CancellationToken.None).ConfigureAwait(false);
        }
        return Result<Entry>.Ok(entry);
    }

    private async Task<Result<Loaded>> StageArchiveAsync(Loaded loaded, PluginPackageIdentity package, CancellationToken ct)
    {
        var index = loaded.Index;
        if (index.Entries.Any(entry => string.Equals(Identity(entry).Sha256, package.Sha256, StringComparison.Ordinal))
            || index.RetiredArchives?.Any(archive => string.Equals(archive.Sha256, package.Sha256, StringComparison.Ordinal)) == true)
            return Result<Loaded>.Ok(loaded);

        // Record cleanup intent before creating bytes that the install commit may never adopt.
        var pending = WithRetiredArchive(index, index.Entries, package);
        var committed = await CommitAsync(loaded, pending, ct).ConfigureAwait(false);
        return committed.IsOk
            ? await LoadAsync(ct).ConfigureAwait(false)
            : Result<Loaded>.Fail(committed.Error!);
    }

    private async Task<Result<Unit>> StopAsync(Loaded loaded, PluginPackageIdentity identity, List<PluginAuthorityChangedEventArgs> notifications, CancellationToken ct)
    {
        var entry = loaded.Index.Entries.SingleOrDefault(x => Identity(x) == identity);
        if (entry is null) return Failure<Unit>("Installed package changed.");
        if (!entry.Enabled && entry.CleanupThrough is null && !_blocked.Contains(identity.Id)) return Result<Unit>.Ok(default);
        _cache = null;
        _blocked.Add(identity.Id);
        var through = entry.CleanupThrough ?? entry.Generation;
        Result<Unit> persisted = Result<Unit>.Ok(default);
        var pendingGeneration = entry.Generation;
        if (entry.CleanupThrough is null)
        {
            var stopped = entry with { Enabled = false, Generation = loaded.Index.NextGeneration, CleanupThrough = through };
            var index = loaded.Index with { NextGeneration = loaded.Index.NextGeneration + 1, Entries = [.. loaded.Index.Entries.Select(x => x == entry ? stopped : x)] };
            try { persisted = await CommitAsync(loaded, index, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                persisted = Failure<Unit>("Unable to persist package revocation.");
            }
            pendingGeneration = stopped.Generation;
            notifications.Add(new(identity.Id, stopped.Generation));
        }
        // RevokeAsync establishes its binding barrier synchronously. Never hold the manager gate
        // while foreign teardown runs, including when persisting the disabled state failed.
        _gate.Release();
        Result<Unit> cleanup;
        try { cleanup = await _revocation.RevokeAsync(identity, through, CancellationToken.None).ConfigureAwait(false); }
        finally { await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false); }
        if (!persisted.IsOk) return persisted;
        if (!cleanup.IsOk) return cleanup;
        var latest = await LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (!latest.IsOk) return Result<Unit>.Fail(latest.Error!);
        var pending = latest.Value!.Index.Entries.SingleOrDefault(x => Identity(x) == identity && x.CleanupThrough == through && x.Generation == pendingGeneration);
        if (pending is null) return Failure<Unit>("Package changed during cleanup.");
        var cleared = latest.Value.Index with { Entries = [.. latest.Value.Index.Entries.Select(x => x == pending ? pending with { CleanupThrough = null } : x)] };
        var committed = await CommitAsync(latest.Value, cleared, CancellationToken.None).ConfigureAwait(false);
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
            if (!loaded.IsOk) return Result<IReadOnlyList<PluginInstallation>>.Fail(loaded.Error!);
            await RetryRetiredArchivesAsync(loaded.Value!, ct).ConfigureAwait(false);
            return HasAuthority
                ? Result<IReadOnlyList<PluginInstallation>>.Ok(loaded.Value!.Installations.Values.ToArray())
                : Failure<IReadOnlyList<PluginInstallation>>("Plugin profile authority is unavailable.");
        }
        finally { _gate.Release(); }
    }

    public async Task<Result<PluginInstallation?>> FindAsync(string pluginId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            return loaded.IsOk
                ? Result<PluginInstallation?>.Ok(loaded.Value!.Installations.GetValueOrDefault(pluginId))
                : Result<PluginInstallation?>.Fail(loaded.Error!);
        }
        finally { _gate.Release(); }
    }

    public async Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct)
    {
        var item = await FindAsync(pluginId, ct).ConfigureAwait(false);
        return item.IsOk ? Result<PluginGrantSnapshot?>.Ok(item.Value?.Enabled == true ? item.Value.Grant : null) : Result<PluginGrantSnapshot?>.Fail(item.Error!);
    }

    public async Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct)
    {
        var notifications = new List<PluginAuthorityChangedEventArgs>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            var entry = loaded.Value!.Index.Entries.SingleOrDefault(x => Identity(x) == package);
            if (entry is null) return Failure<Unit>("Installed package changed.");
            if (!enabled) return await StopAsync(loaded.Value, package, notifications, ct).ConfigureAwait(false);
            if (_blocked.Contains(package.Id) || entry.CleanupThrough is not null) return Failure<Unit>("Package cleanup is pending.");
            if (entry.Enabled) return Result<Unit>.Ok(default);
            var updated = entry with { Enabled = true, Generation = loaded.Value.Index.NextGeneration };
            var index = loaded.Value.Index with { NextGeneration = loaded.Value.Index.NextGeneration + 1, Entries = [.. loaded.Value.Index.Entries.Select(x => x == entry ? updated : x)] };
            var result = await CommitAsync(loaded.Value, index, ct).ConfigureAwait(false);
            if (result.IsOk) notifications.Add(new(package.Id, updated.Generation));
            return result;
        }
        finally
        {
            _gate.Release();
            foreach (var notification in notifications) Changed?.Invoke(this, notification);
        }
    }

    public async Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct)
    {
        var notifications = new List<PluginAuthorityChangedEventArgs>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            if (loaded.Value!.Index.RetiredArchives?.Contains(package) == true)
                return await DeleteRetiredArchiveAsync(loaded.Value, package, ct).ConfigureAwait(false);

            var stopped = await StopAsync(loaded.Value, package, notifications, ct).ConfigureAwait(false);
            if (!stopped.IsOk) return stopped;
            loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            var index = WithRetiredArchive(loaded.Value!.Index,
                [.. loaded.Value.Index.Entries.Where(x => Identity(x) != package)], package);
            var saved = await CommitAsync(loaded.Value, index, ct).ConfigureAwait(false);
            if (!saved.IsOk) return saved;
            notifications.Add(new(package.Id, index.NextGeneration));
            var latest = await LoadAsync(ct).ConfigureAwait(false);
            return latest.IsOk
                ? await DeleteRetiredArchiveAsync(latest.Value!, package, ct).ConfigureAwait(false)
                : Result<Unit>.Fail(latest.Error!);
        }
        finally
        {
            _gate.Release();
            foreach (var notification in notifications) Changed?.Invoke(this, notification);
        }
    }

    private static Index WithRetiredArchive(Index index, List<Entry> entries, PluginPackageIdentity? retired)
    {
        var installedHashes = entries.Select(entry => Identity(entry).Sha256).ToHashSet(StringComparer.Ordinal);
        var pending = (index.RetiredArchives ?? []).Where(archive => !installedHashes.Contains(archive.Sha256)).ToList();
        if (retired is not null && !installedHashes.Contains(retired.Sha256)
            && pending.All(archive => !string.Equals(archive.Sha256, retired.Sha256, StringComparison.Ordinal)))
        {
            pending.Add(retired);
        }
        return index with { Entries = entries, RetiredArchives = pending };
    }

    private async Task RetryRetiredArchivesAsync(Loaded loaded, CancellationToken ct)
    {
        if (loaded.Index.RetiredArchives is not { Count: > 0 } pending) return;

        var remaining = new List<PluginPackageIdentity>();
        foreach (var archive in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (!HasAuthority) return;
            if (!(await _store.DeleteContentAsync(_options.Profile, archive, ct).ConfigureAwait(false)).IsOk)
                remaining.Add(archive);
        }
        if (remaining.Count < pending.Count)
            _ = await CommitAsync(loaded, loaded.Index with { RetiredArchives = remaining }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> DeleteRetiredArchiveAsync(Loaded loaded, PluginPackageIdentity package, CancellationToken ct)
    {
        var pending = loaded.Index.RetiredArchives;
        if (pending is null || !pending.Contains(package)) return Result<Unit>.Ok(default);
        if (!HasAuthority) return Failure<Unit>("Plugin profile authority is unavailable.");

        var deleted = await _store.DeleteContentAsync(_options.Profile, package, ct).ConfigureAwait(false);
        if (!deleted.IsOk) return deleted;
        var next = loaded.Index with { RetiredArchives = [.. pending.Where(archive => archive != package)] };
        return await CommitAsync(loaded, next, CancellationToken.None).ConfigureAwait(false);
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
            return HasAuthority ? Result<Stream>.Ok(new MemoryStream(bytes.ToArray(), writable: false)) : Failure<Stream>("Plugin profile authority is unavailable.");
        }
        finally { _gate.Release(); }
    }
}
