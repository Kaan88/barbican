// Mirrors: Rust reader.rs:27 header, :33 headers_all, :38 unfold; RFC 5322 section 2.2.3 (unfolding); Bulwark lib/email-headers.ts:625-660.
// Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Reader;

/// <summary>Reads header values from the JMAP header list ({"name", "value"} objects).</summary>
public static class ReaderHeaders
{
    /// <summary>The first value of a header (name case-insensitive), unfolded and trimmed, or null.</summary>
    public static string? Header(JsonArray headers, string name)
    {
        throw new PortPendingException("reader.rs:27 header");
    }

    /// <summary>Every value of a header (name case-insensitive), in order.</summary>
    public static IReadOnlyList<string> HeadersAll(JsonArray headers, string name)
    {
        throw new PortPendingException("reader.rs:33 headers_all");
    }
}
