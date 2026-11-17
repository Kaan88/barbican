// Mirrors: Rust reader.rs:143 dkim_pass (the topmost Authentication-Results, an aligned DKIM pass for the From domain); RFC 8601, RFC 6376;
// user decision D-9 and R2-07 (only the account server's own header counts; the pinned authserv-id is the reader stream's work). Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.AuthResults;

/// <summary>The gate that lets one-click unsubscribe run only for a sender whose DKIM signature vouches for the From domain.</summary>
public static class DkimGate
{
    /// <summary>True when the topmost Authentication-Results has dkim=pass with a signing domain aligned to the domain of <paramref name="from"/>.</summary>
    public static bool DkimPass(JsonArray headers, string from)
    {
        throw new PortPendingException("reader.rs:143 dkim_pass");
    }
}
