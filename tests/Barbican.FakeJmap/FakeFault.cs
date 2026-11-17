// Fault injection of the fake server (M1-plan §6.1): HTTP statuses, RFC 7807 problems, /queryChanges failures. Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>What the next matching request meets.</summary>
public enum FakeFault
{
    /// <summary>401 on the next request carrying credentials.</summary>
    Unauthorized,

    /// <summary>429 with Retry-After on the next API request.</summary>
    TooManyRequests,

    /// <summary>503 with Retry-After on the next API request.</summary>
    Unavailable,

    /// <summary>An RFC 7807 problem (500, urn:ietf:params:jmap:error:serverFail style) on the next API request.</summary>
    Problem,

    /// <summary>The next Email/queryChanges answers cannotCalculateChanges.</summary>
    CannotCalculateChanges,

    /// <summary>The next Email/queryChanges answers tooManyChanges.</summary>
    TooManyChanges,

    /// <summary>The next Email/queryChanges reports a newQueryState older than its sinceQueryState.</summary>
    QueryStateWentBack,

    /// <summary>The next Email/changes answers cannotCalculateChanges.</summary>
    EmailChangesFail,
}
