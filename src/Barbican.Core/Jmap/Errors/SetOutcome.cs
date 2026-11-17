// Mirrors: RFC 8620 §5.3 (the /set response: created, updated, destroyed, notCreated, notUpdated, notDestroyed);
// Rust jmap.rs:337 update (updated count and failure descriptions), :1344 created_id. Owner jmap.

namespace Barbican.Core.Jmap.Errors;

/// <summary>The result maps of one <c>/set</c> response.</summary>
/// <param name="Created">Server id by creation id.</param>
/// <param name="Updated">Ids that were updated.</param>
/// <param name="Destroyed">Ids that were destroyed.</param>
/// <param name="Failed">One error per object the server refused.</param>
public sealed record SetOutcome(
    IReadOnlyDictionary<string, string> Created,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Destroyed,
    IReadOnlyList<SetError> Failed);
