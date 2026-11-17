// Mirrors: Rust main.rs:612 human_size ("1.2 MB", "3 KB", "12 B"; decimal units). Stub from F2; owner mailcore.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Attachments;

/// <summary>Sizes as the list and the cards show them.</summary>
public static class ByteSize
{
    /// <summary>Decimal units: one decimal from 1 MB, whole KB from 1 KB, else bytes.</summary>
    public static string Human(long bytes) => throw new PortPendingException("ByteSize.Human: mailcore stream");
}
