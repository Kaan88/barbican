// One entry of the fake change log (Stalwart keeps a change log per account; a state is the last change id, RFC 8620 §5.2). Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>A logged change.</summary>
/// <param name="ChangeId">The account-wide change id.</param>
/// <param name="Type">The data type (Email, Mailbox, Thread).</param>
/// <param name="Id">The object id.</param>
/// <param name="Kind">created, updated or destroyed.</param>
/// <param name="Properties">For a Mailbox update that touched only these properties; null otherwise.</param>
public sealed record FakeChange(ulong ChangeId, string Type, string Id, string Kind, IReadOnlyList<string>? Properties = null);
