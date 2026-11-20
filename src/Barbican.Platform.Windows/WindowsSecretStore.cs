// Mirrors: ADR 0008 decision 1 (generic credentials "Barbican.Next/<profile>/<name>", CRED_PERSIST_LOCAL_MACHINE, UTF-8 in the blob; the Rust
// entries are never touched); PORT-PLAN.md §3.9 (secrets row). Rust used the keyring crate (keyring 3.6.3 windows.rs: CredWriteW with
// CRED_PERSIST_ENTERPRISE and a UTF-16 blob). Buffers holding a secret are zeroed after use.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform.Windows;

/// <summary>Secrets in the Windows Credential Manager, under this profile's own target names.</summary>
public sealed class WindowsSecretStore(string profile) : IGuardedSecretStore
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The profile whose entries this store reads and writes.</summary>
    public string Profile { get; } = profile;

    /// <inheritdoc/>
    public Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct) => Task.Run(() => Read(SecretTarget.Format(Profile, name)), ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Task.Run(() => Write(SecretTarget.Format(Profile, name), name.Kind, value), ct);
    }

    public Task<Result<Unit>> WriteWhileCurrentAsync(SecretName name, string value, Func<Func<bool>, bool> publishWhileCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(publishWhileCurrent);
        return Task.Run(() =>
        {
            Result<Unit>? result = null;
            var published = publishWhileCurrent(() =>
            {
                if (ct.IsCancellationRequested) return false;
                result = Write(SecretTarget.Format(Profile, name), name.Kind, value);
                return true;
            });
            return published && result is { } saved ? saved :
                Result<Unit>.Fail(new ValidationError("Secret write approval is no longer current."));
        }, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct) => Task.Run(() => Delete(SecretTarget.Format(Profile, name)), ct);

    /// <summary>A copy of the raw blob of any generic credential, or null when none exists (the legacy import reads the Rust entries this way).</summary>
    internal static unsafe byte[]? ReadBlob(string target)
    {
        if (!CredentialApi.CredRead(target, CredentialApi.TypeGeneric, 0, out var cred))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == CredentialApi.ErrorNotFound ? null : throw new IOException($"CredRead failed ({error}).");
        }

        try
        {
            var blob = new byte[cred->CredentialBlobSize];
            new ReadOnlySpan<byte>(cred->CredentialBlob, (int)cred->CredentialBlobSize).CopyTo(blob);
            return blob;
        }
        finally
        {
            if (cred->CredentialBlob is not null)
            {
                new Span<byte>(cred->CredentialBlob, (int)cred->CredentialBlobSize).Clear();
            }

            CredentialApi.CredFree(cred);
        }
    }

    private static Result<string?> Read(string target)
    {
        byte[]? blob;
        try
        {
            blob = ReadBlob(target);
        }
        catch (IOException e)
        {
            return Result<string?>.Fail(new TransportError(e.Message, Retryable: false));
        }

        if (blob is null)
        {
            return Result<string?>.Ok(null);
        }

        try
        {
            return Result<string?>.Ok(Utf8.GetString(blob));
        }
        catch (DecoderFallbackException)
        {
            return Result<string?>.Fail(new TransportError($"The secret {target} is not UTF-8.", Retryable: false));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    private static unsafe Result<Unit> Write(string target, string user, string value)
    {
        var bytes = Utf8.GetBytes(value);
        try
        {
            if (bytes.Length > CredentialApi.MaxBlob)
            {
                return Result<Unit>.Fail(new TransportError($"The secret is too large for the credential store ({bytes.Length} of {CredentialApi.MaxBlob} bytes).", Retryable: false));
            }

            fixed (char* t = target)
            fixed (char* u = user)
            fixed (char* c = "Barbican")
            fixed (byte* b = bytes)
            {
                var cred = new CredentialApi.Credential
                {
                    Type = CredentialApi.TypeGeneric,
                    TargetName = t,
                    UserName = u,
                    Comment = c,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = b,
                    Persist = CredentialApi.PersistLocalMachine,
                };
                return CredentialApi.CredWrite(&cred, 0)
                    ? Result<Unit>.Ok(default)
                    : Result<Unit>.Fail(new TransportError($"CredWrite failed ({Marshal.GetLastPInvokeError()}).", Retryable: false));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static Result<Unit> Delete(string target)
    {
        if (CredentialApi.CredDelete(target, CredentialApi.TypeGeneric, 0))
        {
            return Result<Unit>.Ok(default);
        }

        var error = Marshal.GetLastPInvokeError();
        return error == CredentialApi.ErrorNotFound
            ? Result<Unit>.Ok(default)
            : Result<Unit>.Fail(new TransportError($"CredDelete failed ({error}).", Retryable: false));
    }
}
