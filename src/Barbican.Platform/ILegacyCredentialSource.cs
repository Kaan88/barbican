// Mirrors: ADR 0008 "What the Rust app stores" (generic credentials "<user>.Barbican", the blob UTF-16LE without a terminator);
// spikes/s5-credentials/read-format.ps1 (CredReadW, read only). Windows: WindowsLegacyCredentialSource. Linux: none (ADR 0008 M0 review:
// the Linux import is dropped unless the format is confirmed on the VM).

namespace Barbican.Platform;

/// <summary>Reads one of the Rust app's credential blobs, read only.</summary>
public interface ILegacyCredentialSource
{
    /// <summary>
    /// A copy of the blob of the generic credential <paramref name="target"/>, or null when there is none. The caller zeroes the array after
    /// use.
    /// </summary>
    byte[]? Read(string target);
}
