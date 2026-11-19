// Host-added M6b profile ownership; Bulwark stores/plugin-store.ts:188-320 assumes a single browser authority.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Storage;
namespace Barbican.Core.Plugins.Persistence;

/// <summary>Acquires an exclusive OS file handle under the host-selected profile.</summary>
public sealed class PluginProfileAuthorityLeaseFactory : IPluginProfileAuthorityLeaseFactory
{
    public Task<Result<IPluginProfileAuthorityLease>> AcquireAsync(IProfilePaths paths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!ProfilePaths.IsValidName(paths.Profile) || !Path.IsPathFullyQualified(paths.ConfigDir)) return Task.FromResult(Fail());
        return Task.Run(() => Acquire(paths, ct), ct);
    }
    private static Result<IPluginProfileAuthorityLease> Acquire(IProfilePaths paths, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.ConfigDir));
            var directory = Path.Combine(root, "plugins");
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return Fail();
            var path = Path.Combine(directory, "authority.lock");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return Fail();
            var handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (ct.IsCancellationRequested) { handle.Dispose(); ct.ThrowIfCancellationRequested(); }
            return Result<IPluginProfileAuthorityLease>.Ok(new Lease(paths.Profile, root, handle));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Fail(); }
    }
    private static Result<IPluginProfileAuthorityLease> Fail() => Result<IPluginProfileAuthorityLease>.Fail(new ValidationError("Plugin profile authority is unavailable."));
    private sealed class Lease : IPluginProfileAuthorityLease
    {
        private readonly FileStream _file;
        private readonly CancellationTokenSource _stop = new();
        private readonly CancellationToken _lifetime;
        private int _held = 1;
        private readonly Lock _closeGate = new();
        private Task? _closing;
        public Lease(string profile, string configRoot, FileStream file) { Profile = profile; ConfigRoot = configRoot; _file = file; _lifetime = _stop.Token; }
        public string Profile { get; }
        public string ConfigRoot { get; }
        public bool IsHeld => Volatile.Read(ref _held) != 0;
        public CancellationToken Lifetime => _lifetime;
        public ValueTask DisposeAsync()
        {
            lock (_closeGate)
            {
                if (_closing is not null) return new ValueTask(_closing);
                Volatile.Write(ref _held, 0);
                return new ValueTask(_closing = CloseAsync());
            }
        }
        private async Task CloseAsync()
        {
            try { await _stop.CancelAsync().ConfigureAwait(false); }
            finally { await _file.DisposeAsync().ConfigureAwait(false); _stop.Dispose(); }
        }
    }
}
