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

    /// <summary>A one-time code the account requires (Stalwart: appended to the password as <c>password$code</c>); null: none.</summary>
    public string? Otp { get; init; }

    /// <summary>The mail account id.</summary>
    public string AccountId { get; init; } = "b";

    /// <summary>Shared (group) mail accounts the session lists besides the user's own (isPersonal false; M2 unified). The API does not serve them.</summary>
    public IReadOnlyList<string> SharedAccounts { get; init; } = [];

    /// <summary>Advertise the Stalwart capability (state strings are Stalwart's either way).</summary>
    public bool Stalwart { get; init; } = true;

    /// <summary>Session URLs relative to the session resource (RFC 8620 §2: allowed) instead of absolute.</summary>
    public bool RelativeUrls { get; init; }

    /// <summary>The origin the session names its URLs under (a server behind a proxy advertising its internal name); null: <see cref="Origin"/>.</summary>
    public string? SessionUrlOrigin { get; init; }

    /// <summary>Answer /.well-known/jmap with a 307 to /jmap/session (else the session directly, as Stalwart).</summary>
    public bool RedirectWellKnown { get; init; }

    /// <summary>Redirect target origin for the well-known redirect (null: same origin).</summary>
    public string? RedirectOrigin { get; init; }

    /// <summary>Put the submission capability on the account (with maxDelayedSend) and not on the session root.</summary>
    public long MaxDelayedSend { get; init; } = 3600;

    /// <summary>Advertise an event source.</summary>
    public bool Push { get; init; } = true;

    /// <summary>The most ids one /query returns, whatever limit is asked (RFC 8620 §5.5: a server may cap limit).</summary>
    public int QueryLimitCap { get; init; } = 10_000;

    /// <summary>Advertise the Sieve capability (urn:ietf:params:jmap:sieve) for SieveScript/get, set and validate (M2: quick rules). Off: the M1 session.</summary>
    public bool SieveCapability { get; init; }

    /// <summary>Advertise the FileNode capability (urn:ietf:params:jmap:filenode) for Save to Files (M2 reader). Off: the M1 session.</summary>
    public bool FileNodeCapability { get; init; }

    /// <summary>Advertise the Calendars capability (urn:ietf:params:jmap:calendars) for CalendarEvent/parse (M2 reader). Off: the M1 session.</summary>
    public bool CalendarsCapability { get; init; }

    /// <summary>M4: the calendar data behind Calendar/* and CalendarEvent/* (needs <see cref="CalendarsCapability"/>); null: those methods answer unknownMethod.</summary>
    public FakeCalendars? Calendars { get; init; }

    /// <summary>M4: advertise Principals and Principal availability (Principal/query, Principal/getAvailability, G-009).</summary>
    public bool AvailabilityCapability { get; init; }

    /// <summary>Answer (with <see cref="Stalwart"/>, which advertises <c>urn:stalwart:jmap</c>) x:ArchivedItem/get, query and set (Stalwart's deleted-item archive, J-074). Off: the M1 session.</summary>
    public bool ArchiveCapability { get; init; }

    /// <summary>With <see cref="ArchiveCapability"/>: refuse the <c>accountId</c> filter of x:ArchivedItem/query with unsupportedFilter, as Stalwart does for a user who may not impersonate.</summary>
    public bool ArchiveRefusesAccountFilter { get; init; }

    /// <summary>Add the M2 seed (FakeSeed.FillM2) on top of the M1 seed. Off: the M1 seed only, so every M1 test sees the data it always saw.</summary>
    public bool SeedM2 { get; init; }

    /// <summary>M3 send pipeline: the SMTP extensions the submission capability lists (object form, as Stalwart); FUTURERELEASE makes HOLDFOR work. Empty: the M1 session.</summary>
    public IReadOnlyList<string> SubmissionExtensions { get; init; } = [];

    /// <summary>M3 templates: advertise urn:ietf:params:jmap:metadata and keep mailbox metadata (Mailbox/get metadata, Mailbox/set metadata patches with ifInState).</summary>
    public bool MetadataCapability { get; init; }

    /// <summary>M3: maxSizeAttachmentsPerEmail of the mail capability (RFC 8621 1.3.1); null: not advertised.</summary>
    public long? MaxSizeAttachmentsPerEmail { get; init; }

    /// <summary>M3 identities (Identity/get); null: the one M1 identity i1.</summary>
    public IReadOnlyList<FakeIdentity>? Identities { get; init; }

    /// <summary>M3: refuse a MAIL FROM that is not the identity's address (or covered by its wildcard) with forbiddenFrom, as Stalwart does.</summary>
    public bool StrictMailFrom { get; init; }

    /// <summary>M3: the clock of held submissions (a FakeTimeProvider in tests); null: the system clock.</summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>Add the M3 seed (FakeSeed.FillM3: a reply thread with an inline image, a draft) on top of the M1 seed.</summary>
    public bool SeedM3 { get; init; }

    public long MaxSizeUpload { get; init; } = 50_000_000;

    public int MaxConcurrentUpload { get; init; } = 4;

    public long MaxSizeRequest { get; init; } = 10_000_000;

    public int MaxConcurrentRequests { get; init; } = 4;

    public int MaxCallsInRequest { get; init; } = 16;

    public int MaxObjectsInGet { get; init; } = 500;

    public int MaxObjectsInSet { get; init; } = 500;
}
