// Source: Bulwark lib/plugin-sandbox/storage-scope.ts:82-100 and stores/auth-store.ts sign-out cleanup.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Storage;

namespace Barbican.Platform.Plugins;

public sealed partial class AuthenticatedPluginPackageStore
{
    /// <summary>Erases user storage without opening or creating the package index.</summary>
    public static async Task<Result<Unit>> ClearSignedOutProfileAsync(IProfilePaths paths, IPluginProfileAuthorityLease lease,
        ISecretStore secrets, IReadOnlyList<string> loginKeys, bool allAccounts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(secrets);
        try
        {
            if (!OwnsProfile(paths, lease)) return Fail<Unit>();
            using var storage = new AuthenticatedPluginPackageStore(paths, lease, secrets);
            return await storage.ClearSignedOutStorageAsync(paths.Profile, loginKeys, allAccounts, ct).ConfigureAwait(false);
        }
        catch (ArgumentException) { return Fail<Unit>(); }
    }

    /// <summary>Erases the captured login scopes while the account manager prevents new storage calls.</summary>
    public Task<Result<Unit>> ClearSignedOutStorageAsync(string profile, IReadOnlyList<string> loginKeys, bool allAccounts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loginKeys);
        var keys = loginKeys.ToArray();
        return WithUserGateAsync(profile, async () =>
        {
            if (!await Task.Run(() => allAccounts ? RequireUserRoot(create: false) : HasUserFiles(), ct).ConfigureAwait(false))
                return Result<Unit>.Ok(default);
            if (allAccounts) return await Task.Run(() => ClearAccountScopes(null, ct), ct).ConfigureAwait(false);
            return await UseUserKeyAsync(key =>
                ClearAccountScopes(keys.Select(login => LoginScope(key, login)).ToHashSet(StringComparer.Ordinal), ct), ct).ConfigureAwait(false);
        }, ct);
    }

    private bool HasUserFiles() =>
        RequireUserRoot(create: false) && UserPackageDirectories().Any(directory => Directory.EnumerateFileSystemEntries(directory).Any());

    private string[] UserPackageDirectories()
    {
        var packages = Directory.GetFileSystemEntries(UserRoot);
        if (packages.Any(directory => !IsScopeName(Path.GetFileName(directory)) || !CheckDirectory(directory, false)))
            throw new IOException("Unexpected add-on storage scope.");
        return packages;
    }

    private Result<Unit> ClearAccountScopes(HashSet<string>? logins, CancellationToken ct)
    {
        if (!RequireUserRoot(create: false)) return Result<Unit>.Ok(default);
        var erased = _lease.TryRunWhileHeld(() =>
        {
            if (!Current(_profile) || ct.IsCancellationRequested) return false;
            if (!RequireUserRoot(create: false)) return true;
            foreach (var directory in UserPackageDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if (!ClearUserFiles(directory, logins, ct)) return false;
                if (logins is null) Directory.Delete(directory);
            }
            if (logins is null) Directory.Delete(UserRoot);
            return true;
        });
        return erased ? Result<Unit>.Ok(default) : Fail<Unit>();
    }

    private static bool ClearUserFiles(string directory, HashSet<string>? logins, CancellationToken ct)
    {
        var files = Directory.GetFileSystemEntries(directory);
        if (files.Any(path => !IsUserStorageFile(path))) return false;
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            if (logins is null || BelongsToLogin(path, logins)) File.Delete(path);
        }
        return true;
    }

    private static bool BelongsToLogin(string path, HashSet<string> logins)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length switch
        {
            64 => logins.Contains(name),
            98 => logins.Contains(name.Substring(1, 64)),
            33 => true, // Unscoped staging from the earlier writer is always abandoned under the gate.
            _ => false,
        };
    }
}
