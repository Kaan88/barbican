// Mirrors Bulwark lib/jmap/client.ts:8155-8183 (ParticipantIdentity/get and set); Stalwart crates/jmap/src/participant_identity/{get,set}.rs.
namespace Barbican.Core.Calendar;

/// <summary>A calendar address available as an event organizer.</summary>
public sealed record CalendarParticipantIdentity(string Id, string Name, string CalendarAddress, bool IsDefault);
