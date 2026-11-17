// Mirrors: Rust main.rs:612 human_size ("1.2 MB", "3 KB", "12 B"; decimal units). Owner mailcore.

using System.Globalization;

namespace Barbican.Core.Mail.Attachments;

/// <summary>Sizes as the list and the cards show them.</summary>
public static class ByteSize
{
    /// <summary>Decimal units: one decimal from 1 MB, whole KB from 1 KB, else bytes.</summary>
    public static string Human(long bytes)
    {
        var n = Math.Max(bytes, 0);
        if (n >= 1_000_000)
        {
            return (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        return n >= 1000
            ? (n / 1000).ToString(CultureInfo.InvariantCulture) + " KB"
            : n.ToString(CultureInfo.InvariantCulture) + " B";
    }
}
