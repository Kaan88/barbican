// Source: Bulwark stores/plugin-store.ts:467-516; host-added secret authentication and crash-safe index framing.
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Persistence;
namespace Barbican.Platform.Plugins;

public sealed partial class AuthenticatedPluginPackageStore
{
    private async Task<FileStream?> TransactionAsync(CancellationToken ct)
    {
        if (!Current(_profile)) return null;
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(_directory);
            if ((File.GetAttributes(_directory) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return null;
            var path = Path.Combine(_directory, "transaction.lock");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return null;
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }, ct).ConfigureAwait(false);
    }

    private async Task<Result<PluginStoreSnapshot>> ReadLockedAsync(CancellationToken ct)
    {
        var secret = await _secrets.ReadAsync(_secretName, ct).ConfigureAwait(false);
        if (!secret.IsOk || !TrySecret(secret.Value, out var key, out var revision, out var digest)) return Fail<PluginStoreSnapshot>();
        try
        {
            var envelope = await Task.Run(() => ReadBounded(Path.Combine(_directory, "index.dat"), (4 * 1024 * 1024) + 40), ct).ConfigureAwait(false);
            if (envelope is null || envelope.Length < 40 || !Current(_profile)) return Fail<PluginStoreSnapshot>();
            var currentRevision = BinaryPrimitives.ReadInt64LittleEndian(envelope);
            var bodyLength = envelope.Length - 32;
            var mac = HMACSHA256.HashData(key, envelope.AsSpan(0, bodyLength));
            if (currentRevision != revision || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope), digest)
                || !CryptographicOperations.FixedTimeEquals(mac, envelope.AsSpan(bodyLength))) return Fail<PluginStoreSnapshot>();
            return Result<PluginStoreSnapshot>.Ok(new(currentRevision, ImmutableArray.CreateRange(envelope.AsSpan(8, bodyLength - 8).ToArray())));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] MakeEnvelope(long revision, byte[] index, byte[] key)
    {
        var data = new byte[checked(index.Length + 40)];
        BinaryPrimitives.WriteInt64LittleEndian(data, revision);
        index.CopyTo(data.AsSpan(8));
        HMACSHA256.HashData(key, data.AsSpan(0, index.Length + 8)).CopyTo(data.AsSpan(index.Length + 8));
        return data;
    }
    private static string Secret(byte[] key, long revision, byte[] envelope) => string.Concat("v1:", Convert.ToBase64String(key), ":", revision.ToString(CultureInfo.InvariantCulture), ":", Convert.ToHexStringLower(SHA256.HashData(envelope)));
    private static bool TrySecret(string? secret, out byte[] key, out long revision, out byte[] digest)
    {
        key = [];
        digest = [];
        revision = 0;
        if (secret is null || secret.Length > 256) return false;
        var parts = secret.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], "v1", StringComparison.Ordinal) || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision < 0) return false;
        try
        {
            key = Convert.FromBase64String(parts[1]);
            digest = Convert.FromHexString(parts[3]);
            if (key.Length == 32 && digest.Length == 32) return true;
        }
        catch (FormatException) { }
        CryptographicOperations.ZeroMemory(key);
        key = [];
        return false;
    }
    private static byte[]? ReadBounded(string path, int limit)
    {
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) throw new IOException("Unexpected plugin storage link.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limit) throw new IOException("Plugin storage limit exceeded.");
        using var output = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + count > limit) throw new IOException("Plugin storage limit exceeded.");
            output.Write(chunk, 0, count);
        }
        return output.ToArray();
    }
    private static void AtomicWrite(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(content);
                output.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
