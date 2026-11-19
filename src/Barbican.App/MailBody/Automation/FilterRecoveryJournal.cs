#if AUTOMATION_WRITES
// L-040: private, bounded recovery data before synthetic filter writes. No journal data enters the automation response.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Barbican.Core.Storage;
using Barbican.Platform;

namespace Barbican.App.MailBody.Automation;

/// <summary>An encrypted recovery record. A persistent marker prevents another run until recovery is completed.</summary>
public sealed class FilterRecoveryJournal : IDisposable
{
    private const int MaxPlainBytes = 128 * 1024;
    private readonly ISecretStore _secrets;
    private readonly SecretName _name;
    private readonly string _path;
    private readonly string _marker;
    private readonly byte[] _key;

    private FilterRecoveryJournal(ISecretStore secrets, SecretName name, string path, string marker, byte[] key)
    {
        _secrets = secrets;
        _name = name;
        _path = path;
        _marker = marker;
        _key = key;
    }

    /// <summary>Creates a unique protected journal; an existing marker always blocks a new run.</summary>
    public static async Task<FilterRecoveryJournal> CreateAsync(ISecretStore secrets, string profileDir, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentException.ThrowIfNullOrEmpty(profileDir);
        Directory.CreateDirectory(profileDir);
        var id = Guid.NewGuid().ToString("N");
        var marker = Path.Combine(profileDir, "filter-recovery.lock");
        var path = Path.Combine(profileDir, $"filter-recovery-{id}.json");
        using (var file = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var markerBytes = Encoding.ASCII.GetBytes(id);
            await file.WriteAsync(markerBytes, ct).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }

        var name = new SecretName("filter-recovery", id);
        var found = await secrets.ReadAsync(name, ct).ConfigureAwait(false);
        if (!found.IsOk || found.Value is not null || File.Exists(path))
            throw new InvalidOperationException("Cannot create a unique filter recovery record; inspect the retained marker.");
        var key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        var written = await secrets.WriteAsync(name, encoded, ct).ConfigureAwait(false);
        var checkedKey = await secrets.ReadAsync(name, ct).ConfigureAwait(false);
        if (!written.IsOk || !checkedKey.IsOk || !string.Equals(encoded, checkedKey.Value, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("Cannot verify the private recovery key; inspect the retained marker.");
        }
        return new FilterRecoveryJournal(secrets, name, path, marker, key);
    }

    /// <summary>Opens an existing marked recovery record after a restart.</summary>
    public static async Task<FilterRecoveryJournal> OpenAsync(ISecretStore secrets, string profileDir, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentException.ThrowIfNullOrEmpty(profileDir);
        var marker = Path.Combine(profileDir, "filter-recovery.lock");
        var id = await File.ReadAllTextAsync(marker, ct).ConfigureAwait(false);
        if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException("The recovery marker is invalid.");
        var name = new SecretName("filter-recovery", id);
        var read = await secrets.ReadAsync(name, ct).ConfigureAwait(false);
        if (!read.IsOk || read.Value is null) throw new InvalidOperationException("The recovery key is unavailable.");
        var key = Convert.FromBase64String(read.Value);
        if (key.Length != 32) { CryptographicOperations.ZeroMemory(key); throw new InvalidOperationException("The recovery key is invalid."); }
        return new FilterRecoveryJournal(secrets, name, Path.Combine(profileDir, $"filter-recovery-{id}.json"), marker, key);
    }

    /// <summary>Authenticates and reads a retained recovery record.</summary>
    public async Task<string> ReadAsync(CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
        if (text.Length > (MaxPlainBytes * 2)) throw new InvalidOperationException("The recovery record exceeds the limit.");
        var parsed = JsonSerializer.Deserialize<Envelope>(text) ?? throw new InvalidOperationException("The recovery record is invalid.");
        var ciphertext = Convert.FromBase64String(parsed.Ciphertext);
        if (ciphertext.Length > MaxPlainBytes) throw new InvalidOperationException("The recovery record exceeds the limit.");
        var plain = new byte[ciphertext.Length];
        try
        {
            using (var aes = new AesGcm(_key, 16)) aes.Decrypt(Convert.FromBase64String(parsed.Nonce), ciphertext, Convert.FromBase64String(parsed.Tag), plain);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>Atomically writes and reads back the authenticated record before a subsequent mutation.</summary>
    public async Task SaveAsync(string plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        if (plain.Length > MaxPlainBytes) throw new InvalidOperationException("Filter recovery record exceeds the limit.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plain.Length];
        var tag = new byte[16];
        try
        {
            using (var aes = new AesGcm(_key, tag.Length)) aes.Encrypt(nonce, plain, ciphertext, tag);
            var envelope = JsonSerializer.Serialize(new Envelope(Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext)));
            await AtomicFile.WriteAllTextAsync(_path, envelope, ct).ConfigureAwait(false);
            var actual = await Task.Run(() => AtomicFile.ReadAllTextOrNull(_path), ct).ConfigureAwait(false);
            var parsed = actual is null ? null : JsonSerializer.Deserialize<Envelope>(actual);
            if (parsed is null) throw new InvalidOperationException("Cannot verify the recovery record.");
            var restored = new byte[plain.Length];
            using (var aes = new AesGcm(_key, tag.Length)) aes.Decrypt(Convert.FromBase64String(parsed.Nonce), Convert.FromBase64String(parsed.Ciphertext), Convert.FromBase64String(parsed.Tag), restored);
            if (!CryptographicOperations.FixedTimeEquals(plain, restored)) throw new InvalidOperationException("Cannot verify the recovery record.");
            CryptographicOperations.ZeroMemory(restored);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Remove recovery material only after the caller has verified full cleanup.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        var deleted = await _secrets.DeleteAsync(_name, ct).ConfigureAwait(false);
        if (!deleted.IsOk) throw new InvalidOperationException("Could not remove the recovery key; the marker remains.");
        await Task.Run(() => { AtomicFile.Delete(_path); AtomicFile.Delete(_marker); }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);

    private sealed record Envelope(string Nonce, string Tag, string Ciphertext);
}
#endif
