// Mirrors: Rust oauth.rs:277 RefreshLock (a lock file in the config dir, created exclusively; waits up to 15 s, a lock older than 60 s is
// stale and removed; released on drop). spec B-OAUTH-015. The file sits in this profile's config dir, so a C# run never contends with the
// Rust app's lock (they never share a refresh token: ADR 0008).

using Barbican.Core.Storage;

namespace Barbican.Core.Auth;

/// <summary>The cross-process lock held while a refresh token is used.</summary>
public sealed class RefreshLock : IDisposable
{
    /// <summary>The lock file name.</summary>
    public const string FileName = "oauth-refresh.lock";

    /// <summary>How long to wait for another process (Rust: 15 s).</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>Age after which a lock is stale (Rust: 60 s).</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromSeconds(60);

    private readonly string _path;
    private FileStream? _file;

    private RefreshLock(string path, FileStream file)
    {
        _path = path;
        _file = file;
    }

    /// <summary>Takes the lock, or returns null after <see cref="Wait"/> (the refresh then runs without it, as in Rust).</summary>
    public static async Task<RefreshLock?> AcquireAsync(IProfilePaths paths, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        var path = Path.Combine(paths.ConfigDir, FileName);
        var start = time.GetTimestamp();
        while (true)
        {
            var taken = await Task.Run(() => TryTake(path, time), ct).ConfigureAwait(false);
            if (taken is not null)
            {
                return new RefreshLock(path, taken);
            }

            if (time.GetElapsedTime(start) > Wait)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), time, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _file, null) is { } f)
        {
            f.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
                // another process may already treat it as stale
            }
        }
    }

    private static FileStream? TryTake(string path, TimeProvider time)
    {
        IoThreadCheck.AssertNotUiThread();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Delete);
        }
        catch (IOException) when (File.Exists(path))
        {
            try
            {
                if (time.GetUtcNow() - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) > Stale)
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // held open by its owner: not stale after all
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
