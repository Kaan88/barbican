// The knobs of the fake server (M1-plan §6.1): limits as a Stalwart account advertises them, URL style, redirects. Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>How the fake server presents itself.</summary>
public sealed record FakeJmapOptions
{
    /// <summary>The server origin.</summary>
    public string Origin { get; init; } = "https://jmap.example.org";

    /// <summary>The login.</summary>
    public string Username { get; init; } = "user@example.org";

    /// <summary>The password Basic auth accepts.</summary>
    public string Password { get; init; } = "fake-password";

    /// <summary>The mail account id.</summary>
    public string AccountId { get; init; } = "b";

    /// <summary>Advertise the Stalwart capability (state strings are Stalwart's either way).</summary>
    public bool Stalwart { get; init; } = true;

    /// <summary>Session URLs relative to the session resource (RFC 8620 §2: allowed) instead of absolute.</summary>
    public bool RelativeUrls { get; init; }

    /// <summary>Answer /.well-known/jmap with a 307 to /jmap/session (else the session directly, as Stalwart).</summary>
    public bool RedirectWellKnown { get; init; }

    /// <summary>Redirect target origin for the well-known redirect (null: same origin).</summary>
    public string? RedirectOrigin { get; init; }

    /// <summary>Put the submission capability on the account (with maxDelayedSend) and not on the session root.</summary>
    public long MaxDelayedSend { get; init; } = 3600;

    /// <summary>Advertise an event source.</summary>
    public bool Push { get; init; } = true;

    public long MaxSizeUpload { get; init; } = 50_000_000;

    public int MaxConcurrentUpload { get; init; } = 4;

    public long MaxSizeRequest { get; init; } = 10_000_000;

    public int MaxConcurrentRequests { get; init; } = 4;

    public int MaxCallsInRequest { get; init; } = 16;

    public int MaxObjectsInGet { get; init; } = 500;

    public int MaxObjectsInSet { get; init; } = 500;
}
