// Mirrors: Rust cal_editor.rs:1180 principal_ids (Principal/query by email), :1213 availability (Principal/getAvailability), :1258 refresh_availability, :1332 find; JMAP Principals Availability (urn:ietf:params:jmap:principals:availability);
// Bulwark components/calendar/find-a-time.tsx. G-009: without the capability the editor shows no availability and no "Find a time". Contract from the M4 plan section 4.3; owner cal-edit.

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Free/busy of guests (Principal/getAvailability).</summary>
public interface IAvailabilityService
{
    /// <summary>Whether the login's server offers availability at all.</summary>
    bool IsSupported(string accountKey);

    /// <summary>Busy periods (UTC ms start, end, tentative) per guest address (lower case) for [fromMs, toMs); a guest whose principal is not found or whose availability is refused is reported with its error type in the string.</summary>
    Task<Result<IReadOnlyDictionary<string, Result<IReadOnlyList<(long Start, long End, bool Tentative)>>>>> BusyAsync(string accountKey, IReadOnlyList<string> emails, long fromMs, long toMs, CancellationToken ct);
}
