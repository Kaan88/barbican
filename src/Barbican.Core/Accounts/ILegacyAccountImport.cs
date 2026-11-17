// Mirrors: ADR 0008 ("Import accounts from the previous Barbican"). Implemented in Barbican.Platform (RustCredentialImport), which reads the
// Rust app's credential entries read-only.

namespace Barbican.Core.Accounts;

/// <summary>The one-time, read-only import of the Rust app's remembered accounts.</summary>
public interface ILegacyAccountImport
{
    /// <summary>Copies the Rust app's accounts into this profile as new entries; never touches the Rust entries.</summary>
    Task<ImportReport> ImportAsync(ImportMode mode, CancellationToken ct);
}
