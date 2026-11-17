// Mirrors: Rust auth.rs aligned_pass :79 / failure :90; Bulwark lib/server-auth-status.ts. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>What the server's checks say about the visible sender.</summary>
public enum AuthVerdict
{
    /// <summary>No usable result.</summary>
    Unknown,

    /// <summary>An aligned pass: the message comes from the From domain.</summary>
    Verified,

    /// <summary>A failure that leaves the From domain unauthenticated.</summary>
    Failed,
}
