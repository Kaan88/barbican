// Mirrors: Rust zip.rs:14 dos_time, :23 unique_names, :37 build (stored entries, UTF-8 names flag 0x0800, CRC-32, central directory, end record);
// PKZIP APPNOTE 4.3; Bulwark lib/files-zip.ts, lib/download-filename.ts (Save all as zip). Stub from stage F of M2; owner reader.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Zip;

/// <summary>Builds a plain .zip in memory (no compression, no dependency).</summary>
public static class ZipArchiveBuilder
{
    /// <summary>DOS (time, date) words of a moment: 2-second resolution, years 1980 to 2107.</summary>
    public static (ushort Time, ushort Date) DosTime(DateTime when)
    {
        throw new PortPendingException("zip.rs:14 dos_time");
    }

    /// <summary>Names made unique in order: the second "a.pdf" becomes "a (1).pdf" (case-insensitive), an empty name becomes the fallback.</summary>
    public static IReadOnlyList<string> UniqueNames(IReadOnlyList<string> names, string fallback)
    {
        throw new PortPendingException("zip.rs:23 unique_names");
    }

    /// <summary>The archive's bytes; more than 65535 files is refused.</summary>
    public static Result<byte[]> Build(IReadOnlyList<ZipEntryData> entries, DateTime when)
    {
        throw new PortPendingException("zip.rs:37 build");
    }
}
