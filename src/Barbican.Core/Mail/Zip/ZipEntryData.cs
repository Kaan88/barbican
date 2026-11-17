// Mirrors: Rust zip.rs:8 Entry; Bulwark lib/files-zip.ts. Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Zip;

/// <summary>One file in the archive.</summary>
#pragma warning disable CA1819 // file bytes
public sealed record ZipEntryData(string Name, byte[] Data);
#pragma warning restore CA1819
