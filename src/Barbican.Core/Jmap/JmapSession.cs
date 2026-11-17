// Mirrors: RFC 8620 §2 (the Session resource); Stalwart crates/jmap/src/api/session.rs. Contract stub from F2; owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>The JMAP session object (accounts, capabilities, URLs, state). Fields are added by the jmap stream.</summary>
public sealed record JmapSession
{
    /// <summary>The API URL (RFC 8620 §2).</summary>
    public string ApiUrl { get; init; } = "";

    /// <summary>The session state (changes when the session resource changes).</summary>
    public string State { get; init; } = "";
}
