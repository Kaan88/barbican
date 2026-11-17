// Mirrors: ADR 0008 (the Rust app's generic credentials "<user>.Barbican"); spikes/s5-credentials/read-format.ps1 (CredReadW only). Read
// only: this type has no write or delete path.

namespace Barbican.Platform.Windows;

/// <summary>Reads the Rust app's credential blobs.</summary>
public sealed class WindowsLegacyCredentialSource : ILegacyCredentialSource
{
    /// <inheritdoc/>
    public byte[]? Read(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.StartsWith(SecretTarget.Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Not a Rust entry.", nameof(target));
        }

        try
        {
            return WindowsSecretStore.ReadBlob(target);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
