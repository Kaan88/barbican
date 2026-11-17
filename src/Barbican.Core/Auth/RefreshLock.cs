// Mirrors: Rust oauth.rs:277 RefreshLock (a lock file in the config dir; waits up to 15 s; released on drop). spec B-OAUTH-015. The file sits in
// this profile's config dir, so a C# run never contends with the Rust app's lock (they never share a refresh token: ADR 0008).
// Deliberate change (Fable review 1 F-04): Rust created the file exclusively and treated one older than 60 s as stale, so a crashed holder
// blocked refreshes for a minute and a slow live holder could lose its lock to a second process. Here the lock is the open handle itself
// (FileShare.None: a sharing lock on Windows, flock on Unix in .NET), which the OS drops the moment the holding process ends; the file stays.

using Barbican.Core.Storage;

namespace Barbican.Core.Auth;

/// <summary>The cross-process lock held while a refresh token is used.</summary>
public sealed class RefreshLock : IDisposable
{
    /// <summary>The lock file name.</summary>
    public const string FileName = "oauth-refresh.lock";

    /// <summary>How long to wait for another process (Rust: 15 s).</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private FileStream? _file;

    private RefreshLock(FileStream file) => _file = file;

    /// <summary>True while this lock holds the handle.</summary>
    public bool IsHeld => Volatile.Read(ref _file) is not null;

    /// <summary>Takes the lock, or returns null after <see cref="Wait"/> (the refresh then runs without it, as in Rust).</summary>
    public static async Task<RefreshLock?> AcquireAsync(IProfilePaths paths, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        var path = Path.Combine(paths.ConfigDir, FileName);
        var start = time.GetTimestamp();
        while (true)
        {
            var taken = await Task.Run(() => TryTake(path), ct).ConfigureAwait(false);
            if (taken is not null)
            {
                return new RefreshLock(taken);
            }

            if (time.GetElapsedTime(start) > Wait)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), time, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Releases the handle (the file stays: deleting it would let a third process lock a new file while a second holds the old one).</summary>
    public void Dispose() => Interlocked.Exchange(ref _file, null)?.Dispose();

    private static FileStream? TryTake(string path)
    {
        IoThreadCheck.AssertNotUiThread();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.None);
        }
        catch (IOException)
        {
            // held by another process (or another lock in this one)
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
