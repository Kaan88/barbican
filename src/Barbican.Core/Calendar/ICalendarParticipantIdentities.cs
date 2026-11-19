// Mirrors Bulwark components/settings/calendar-settings.tsx:24-36,101-119; Stalwart crates/jmap/src/participant_identity/{get,set}.rs.
using Barbican.Core.Common;
namespace Barbican.Core.Calendar;

/// <summary>Calendar participant identities for a signed-in account.</summary>
public interface ICalendarParticipantIdentities
{
    Task<Result<IReadOnlyList<CalendarParticipantIdentity>>> ListAsync(string accountKey, CancellationToken ct);
    Task<Result<Unit>> SetDefaultAsync(string accountKey, string id, CancellationToken ct);
}
