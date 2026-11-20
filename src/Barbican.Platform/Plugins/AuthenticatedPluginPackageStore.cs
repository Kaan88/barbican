// Source: Bulwark stores/plugin-store.ts:188-320, 467-516; authenticated durable profile storage is host-added.
using System.Collections.Immutable;
using System.Security.Cryptography;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Storage;
namespace Barbican.Platform.Plugins;

/// <summary>Secret-authenticated index and hash-addressed archives within a held host profile lease.</summary>
public sealed partial class AuthenticatedPluginPackageStore : IPluginPackageStore, IPluginUserStorageStore, IDisposable
{
    private readonly string _profile;
    private readonly string _directory;
    private readonly IPluginProfileAuthorityLease _lease;
    private readonly ISecretStore _secrets;
    private readonly SecretName _secretName;
    private int _disposed;
    private AuthenticatedPluginPackageStore(IProfilePaths paths, IPluginProfileAuthorityLease lease, ISecretStore secrets)
    {
        _profile = paths.Profile;
        _directory = Path.Combine(Path.GetFullPath(paths.ConfigDir), "plugins", "packages");
        _lease = lease;
        _secrets = secrets;
        _secretName = new SecretName("plugin-authority-v1", paths.Profile);
    }
    private bool Current(string profile) => Volatile.Read(ref _disposed) == 0 && _lease.IsHeld && !_lease.Lifetime.IsCancellationRequested && string.Equals(profile, _profile, StringComparison.Ordinal) && string.Equals(_lease.Profile, _profile, StringComparison.Ordinal);
    private static Result<T> Fail<T>() => Result<T>.Fail(new ValidationError("Authenticated plugin storage is unavailable."));
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    public static async Task<Result<AuthenticatedPluginPackageStore>> OpenAsync(IProfilePaths paths, IPluginProfileAuthorityLease lease, ISecretStore secrets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(secrets);
        try
        {
            if (!OwnsProfile(paths, lease)) return Fail<AuthenticatedPluginPackageStore>();
            var store = new AuthenticatedPluginPackageStore(paths, lease, secrets);
            var opened = await store.InitializeAsync(ct).ConfigureAwait(false);
            return opened.IsOk && store.Current(paths.Profile) ? Result<AuthenticatedPluginPackageStore>.Ok(store) : Fail<AuthenticatedPluginPackageStore>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or CryptographicException) { return Fail<AuthenticatedPluginPackageStore>(); }
    }

    private static bool OwnsProfile(IProfilePaths paths, IPluginProfileAuthorityLease lease) =>
        ProfilePaths.IsValidName(paths.Profile) && Path.IsPathFullyQualified(paths.ConfigDir) &&
        lease.IsHeld && !lease.Lifetime.IsCancellationRequested &&
        string.Equals(paths.Profile, lease.Profile, StringComparison.Ordinal) &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.ConfigDir)), lease.ConfigRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task<Result<Unit>> InitializeAsync(CancellationToken ct)
    {
        using var transaction = await TransactionAsync(ct).ConfigureAwait(false);
        if (transaction is null || !Current(_profile)) return Fail<Unit>();

        var secret = await _secrets.ReadAsync(_secretName, ct).ConfigureAwait(false);
        if (!secret.IsOk || !Current(_profile)) return Fail<Unit>();
        var hasIndex = await Task.Run(() => File.Exists(PrimaryIndexPath) || File.Exists(AlternateIndexPath), ct).ConfigureAwait(false);
        if (hasIndex)
        {
            return secret.Value is not null && (await ReadLockedAsync(ct).ConfigureAwait(false)).IsOk
                ? Result<Unit>.Ok(default) : Fail<Unit>();
        }

        return secret.Value is { } existing
            ? await RestoreEmptyIndexAsync(existing, ct).ConfigureAwait(false)
            : await CreateEmptyIndexAsync(ct).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> CreateEmptyIndexAsync(CancellationToken ct)
    {
        var hasArchives = await Task.Run(() => Directory.EnumerateFiles(_directory, "*.zip").Any(), ct).ConfigureAwait(false);
        if (hasArchives || !Current(_profile)) return Fail<Unit>();

        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var index = MakeEnvelope(0, [], key);
            // If creation stops before the file is written, the stored digest can authorize
            // reconstruction of exactly the empty index.
            var saved = await _secrets.WriteAsync(_secretName, Secret(key, 0, index), ct).ConfigureAwait(false);
            return saved.IsOk ? await WriteEmptyIndexAsync(index, ct).ConfigureAwait(false) : Fail<Unit>();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<Result<Unit>> RestoreEmptyIndexAsync(string secret, CancellationToken ct)
    {
        if (!TrySecret(secret, out var key, out var revision, out var digest)) return Fail<Unit>();

        try
        {
            if (revision != 0) return Fail<Unit>();
            var index = MakeEnvelope(0, [], key);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(index), digest)
                ? await WriteEmptyIndexAsync(index, ct).ConfigureAwait(false) : Fail<Unit>();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<Result<Unit>> WriteEmptyIndexAsync(byte[] index, CancellationToken ct)
    {
        if (!Current(_profile)) return Fail<Unit>();
        await Task.Run(() => AtomicWrite(PrimaryIndexPath, index), ct).ConfigureAwait(false);
        return (await ReadLockedAsync(ct).ConfigureAwait(false)).IsOk ? Result<Unit>.Ok(default) : Fail<Unit>();
    }

    public async Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, CancellationToken ct)
    {
        if (!Current(profile)) return Fail<PluginStoreSnapshot>();
        try
        {
            using var transaction = await TransactionAsync(ct).ConfigureAwait(false);
            if (transaction is null || !Current(profile)) return Fail<PluginStoreSnapshot>();
            var result = await ReadLockedAsync(ct).ConfigureAwait(false);
            return Current(profile) ? result.Map(x => x.Snapshot) : Fail<PluginStoreSnapshot>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException) { return Fail<PluginStoreSnapshot>(); }
    }

    public Task<Result<long>> CommitAsync(string profile, long expectedRevision, ReadOnlyMemory<byte> index, CancellationToken ct)
    {
        if (index.Length > 4 * 1024 * 1024 || !Current(profile)) return Task.FromResult(Fail<long>());
        var copy = index.ToArray(); // Snapshot caller-owned memory before the first await.
        return CommitCoreAsync(profile, expectedRevision, copy, ct);
    }
    private async Task<Result<long>> CommitCoreAsync(string profile, long expectedRevision, byte[] index, CancellationToken ct)
    {
        try
        {
            using var transaction = await TransactionAsync(ct).ConfigureAwait(false);
            if (transaction is null || !Current(profile)) return Fail<long>();
            var previous = await ReadLockedAsync(ct).ConfigureAwait(false);
            if (!previous.IsOk || previous.Value!.Snapshot.Revision != expectedRevision || !Current(profile)) return Fail<long>();
            var secret = await _secrets.ReadAsync(_secretName, ct).ConfigureAwait(false);
            if (!secret.IsOk || !TrySecret(secret.Value, out var key, out _, out _)) return Fail<long>();
            try
            {
                var revision = checked(expectedRevision + 1);
                var envelope = MakeEnvelope(revision, index, key);
                if (!Current(profile)) return Fail<long>();
                // Stage beside the authenticated index. A rejected pointer write leaves
                // the previously committed index intact, and the digest ignores staged bytes.
                var destination = string.Equals(previous.Value.Path, PrimaryIndexPath, StringComparison.Ordinal)
                    ? AlternateIndexPath : PrimaryIndexPath;
                await Task.Run(() => AtomicWrite(destination, envelope), ct).ConfigureAwait(false);
                if (!Current(profile)) return Fail<long>();
                var saved = await _secrets.WriteAsync(_secretName, Secret(key, revision, envelope), ct).ConfigureAwait(false);
                if (!saved.IsOk || !Current(profile)) return Fail<long>();
                var readback = await ReadLockedAsync(ct).ConfigureAwait(false);
                return Current(profile) && readback.IsOk && readback.Value!.Snapshot.Revision == revision && readback.Value.Snapshot.Index.AsSpan().SequenceEqual(index) ? Result<long>.Ok(revision) : Fail<long>();
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException or OverflowException) { return Fail<long>(); }
    }

    public Task<Result<Unit>> PutContentAsync(string profile, PluginPackageIdentity identity, ReadOnlyMemory<byte> archive, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Current(profile) || archive.Length > 5 * 1024 * 1024) return Task.FromResult(Fail<Unit>());
        var copy = archive.ToArray();
        return PutCoreAsync(profile, identity, copy, ct);
    }
    private async Task<Result<Unit>> PutCoreAsync(string profile, PluginPackageIdentity identity, byte[] bytes, CancellationToken ct)
    {
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), identity.Sha256, StringComparison.Ordinal)) return Fail<Unit>();
        try
        {
            using var transaction = await TransactionAsync(ct).ConfigureAwait(false);
            if (transaction is null || !Current(profile)) return Fail<Unit>();
            var path = ContentPath(identity);
            var existing = await Task.Run(() => ReadBounded(path, 5 * 1024 * 1024), ct).ConfigureAwait(false);
            if (existing is not null) return existing.AsSpan().SequenceEqual(bytes) ? Result<Unit>.Ok(default) : Fail<Unit>();
            await Task.Run(() => AtomicWrite(path, bytes), ct).ConfigureAwait(false);
            var readback = await Task.Run(() => ReadBounded(path, 5 * 1024 * 1024), ct).ConfigureAwait(false);
            return Current(profile) && readback is not null && readback.AsSpan().SequenceEqual(bytes) ? Result<Unit>.Ok(default) : Fail<Unit>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return Fail<Unit>(); }
    }

    public async Task<Result<ImmutableArray<byte>>> ReadContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Current(profile)) return Fail<ImmutableArray<byte>>();
        try
        {
            using var transaction = await TransactionAsync(ct).ConfigureAwait(false);
            if (transaction is null || !Current(profile)) return Fail<ImmutableArray<byte>>();
            var bytes = await Task.Run(() => ReadBounded(ContentPath(identity), 5 * 1024 * 1024), ct).ConfigureAwait(false);
            return Current(profile) && bytes is not null && string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), identity.Sha256, StringComparison.Ordinal)
                ? Result<ImmutableArray<byte>>.Ok(ImmutableArray.CreateRange(bytes)) : Fail<ImmutableArray<byte>>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return Fail<ImmutableArray<byte>>(); }
    }

    public async Task<Result<Unit>> DeleteContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Current(profile)) return Fail<Unit>();
        try
        {
            using var transaction = await TransactionAsync(ct).ConfigureAwait(false);
            if (transaction is null || !Current(profile)) return Fail<Unit>();
            await Task.Run(() => File.Delete(ContentPath(identity)), ct).ConfigureAwait(false);
            return Current(profile) ? Result<Unit>.Ok(default) : Fail<Unit>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail<Unit>(); }
    }
    private string ContentPath(PluginPackageIdentity identity) => Path.Combine(_directory, identity.Sha256 + ".zip");
}
