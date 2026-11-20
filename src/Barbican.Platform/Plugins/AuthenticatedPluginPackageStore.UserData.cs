// Source: Bulwark lib/plugin-sandbox/storage-scope.ts:17-23,82-100.
// Native storage uses an encrypted per-login snapshot under the existing profile lease.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Persistence;

namespace Barbican.Platform.Plugins;

public sealed partial class AuthenticatedPluginPackageStore
{
    private const int UserLimit = 8 * 1024 * 1024;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = sizeof(long) + NonceLength;
    private static readonly UTF8Encoding ScopeEncoding = new(false, true);
    private static readonly ConditionalWeakTable<IPluginProfileAuthorityLease, SemaphoreSlim> UserGates = new();

    public Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, string pluginId, string localLoginKey, CancellationToken ct) =>
        WithUserKeyAsync(profile, pluginId, (key, directory) =>
            Result<PluginStoreSnapshot>.Ok(ReadUserSnapshot(UserAddress(key, directory, localLoginKey), key)), ct);

    public Task<Result<long>> CommitAsync(string profile, string pluginId, string localLoginKey, long expectedRevision,
        ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (expectedRevision < 0 || data.Length is 0 or > UserLimit || !Current(profile)) return Task.FromResult(Fail<long>());
        var copy = data.ToArray(); // Caller-owned memory cannot change the issued operation.
        return WithUserKeyAsync(profile, pluginId, (key, directory) =>
            CommitUserSnapshot(UserAddress(key, directory, localLoginKey), key, expectedRevision, copy, ct), ct);
    }

    /// <summary>Removes the stable namespace of an uninstalled package, including all of its account scopes.</summary>
    public Task<Result<Unit>> ClearUserStorageAsync(string profile, string pluginId, CancellationToken ct) =>
        WithUserKeyAsync(profile, pluginId, (_, directory) => ClearUserDirectory(directory, ct), ct);

    private async Task<Result<T>> WithUserKeyAsync<T>(string profile, string pluginId, Func<byte[], string, Result<T>> run, CancellationToken ct)
    {
        if (!Current(profile) || pluginId is not { Length: > 0 and <= 256 } || pluginId.Contains('\0', StringComparison.Ordinal))
            return Fail<T>();
        // A reopened adapter can share this lease with an existing instance.
        var gate = UserGates.GetValue(_lease, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Current(profile)) return Fail<T>();
            var secret = await _secrets.ReadAsync(_secretName, ct).ConfigureAwait(false);
            if (!secret.IsOk || !TrySecret(secret.Value, out var master, out _, out _))
                return Fail<T>();
            try
            {
                return await Task.Run(() =>
                {
                    if (!Current(profile)) return Fail<T>();
                    var key = HMACSHA256.HashData(master, "barbican-plugin-storage-v1"u8);
                    try
                    {
                        var pluginScope = ScopeName(key, "package\0" + profile + "\0" + pluginId);
                        var directory = Path.Combine(_directory, "user-v1", pluginScope);
                        var result = run(key, directory);
                        return Current(profile) ? result : Fail<T>();
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                }, ct).ConfigureAwait(false);
            }
            finally { CryptographicOperations.ZeroMemory(master); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or CryptographicException or OverflowException)
        {
            return Fail<T>();
        }
        finally { gate.Release(); }
    }

    private static string ScopeName(byte[] key, string scope) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, ScopeEncoding.GetBytes(scope)));

    private static string UserAddress(byte[] key, string directory, string login)
    {
        if (login is not { Length: > 0 and <= 16384 }) throw new ArgumentException("Invalid account scope.", nameof(login));
        var name = ScopeName(key, "login\0" + login);
        return Path.Combine(directory, name + ".data");
    }

    private Result<long> CommitUserSnapshot(string path, byte[] key, long expectedRevision, byte[] data, CancellationToken ct)
    {
        if (ReadUserSnapshot(path, key).Revision != expectedRevision) return Fail<long>();
        var revision = checked(expectedRevision + 1);
        var envelope = Encrypt(path, key, revision, data);
        RequireUserDirectory(Path.GetDirectoryName(path)!, create: true);
        var published = false;
        AtomicWrite(path, envelope, temp =>
        {
            published = _lease.TryRunWhileHeld(() =>
            {
                if (!Current(_profile) || ct.IsCancellationRequested) return false;
                if (!RequireUserDirectory(Path.GetDirectoryName(path)!, create: false)) return false;
                File.Move(temp, path, overwrite: expectedRevision != 0);
                return true;
            });
        });
        return published ? Result<long>.Ok(revision) : Fail<long>();
    }

    private PluginStoreSnapshot ReadUserSnapshot(string path, byte[] key)
    {
        if (!RequireUserDirectory(Path.GetDirectoryName(path)!, create: false)) return new(0, []);
        byte[] envelope;
        try
        {
            if (!IsOrdinaryFile(path))
                throw new IOException("Unexpected add-on data file.");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length < HeaderLength + TagLength || input.Length > UserLimit + HeaderLength + TagLength)
                throw new IOException("Invalid add-on storage length.");
            envelope = new byte[(int)input.Length];
            input.ReadExactly(envelope);
            if (input.ReadByte() != -1) throw new IOException("Add-on storage changed while reading.");
        }
        catch (FileNotFoundException) { return new(0, []); }
        return Decrypt(path, key, envelope);
    }

    private Result<Unit> ClearUserDirectory(string directory, CancellationToken ct)
    {
        if (!RequireUserDirectory(directory, create: false)) return Result<Unit>.Ok(default);
        var deleted = _lease.TryRunWhileHeld(() =>
        {
            if (!Current(_profile) || ct.IsCancellationRequested) return false;
            if (!RequireUserDirectory(directory, create: false)) return true;
            var files = Directory.GetFileSystemEntries(directory);
            if (files.Any(path => !IsUserStorageFile(path))) return false;
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                File.Delete(path);
            }
            Directory.Delete(directory);
            return true;
        });
        return deleted ? Result<Unit>.Ok(default) : Fail<Unit>();
    }

    private static bool IsUserStorageFile(string path)
    {
        if (!IsOrdinaryFile(path)) return false;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return (string.Equals(extension, ".data", StringComparison.Ordinal) && name.Length == 64 && name.All(char.IsAsciiHexDigitLower))
            || (string.Equals(extension, ".tmp", StringComparison.Ordinal) && name.Length == 33 && name[0] == '.'
                && Guid.TryParseExact(name.AsSpan(1), "N", out _));
    }

    private static bool IsOrdinaryFile(string path)
    {
        var attributes = File.GetAttributes(path);
        return !attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private bool RequireUserDirectory(string directory, bool create)
    {
        var userRoot = Path.GetDirectoryName(directory)!;
        return CheckDirectory(_lease.ConfigRoot, false) &&
            CheckDirectory(Path.GetDirectoryName(_directory)!, false) &&
            CheckDirectory(_directory, false) &&
            CheckDirectory(userRoot, create) &&
            CheckDirectory(directory, create);
    }

    private static bool CheckDirectory(string path, bool create)
    {
        if (create && !Directory.Exists(path)) Directory.CreateDirectory(path);
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                return true;
            throw new IOException("Unexpected add-on storage directory.");
        }
        catch (Exception error) when (!create && error is (FileNotFoundException or DirectoryNotFoundException)) { return false; }
    }

    private static byte[] Encrypt(string path, byte[] key, long revision, byte[] data)
    {
        var envelope = new byte[HeaderLength + data.Length + TagLength];
        BinaryPrimitives.WriteInt64LittleEndian(envelope, revision);
        var nonce = envelope.AsSpan(sizeof(long), NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using var cipher = new AesGcm(key, TagLength);
        cipher.Encrypt(nonce, data, envelope.AsSpan(HeaderLength, data.Length), envelope.AsSpan(HeaderLength + data.Length), AssociatedData(path, revision));
        return envelope;
    }

    private static PluginStoreSnapshot Decrypt(string path, byte[] key, byte[] envelope)
    {
        var revision = BinaryPrimitives.ReadInt64LittleEndian(envelope);
        if (revision <= 0) throw new FormatException("Invalid add-on storage revision.");
        var data = new byte[envelope.Length - HeaderLength - TagLength];
        using var cipher = new AesGcm(key, TagLength);
        cipher.Decrypt(envelope.AsSpan(sizeof(long), NonceLength), envelope.AsSpan(HeaderLength, data.Length),
            envelope.AsSpan(HeaderLength + data.Length), data, AssociatedData(path, revision));
        return new(revision, ImmutableCollectionsMarshal.AsImmutableArray(data));
    }

    private static byte[] AssociatedData(string path, long revision)
    {
        var text = Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path);
        var bytes = new byte[sizeof(long) + Encoding.ASCII.GetByteCount(text)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, revision);
        _ = Encoding.ASCII.GetBytes(text, bytes.AsSpan(sizeof(long)));
        return bytes;
    }
}
