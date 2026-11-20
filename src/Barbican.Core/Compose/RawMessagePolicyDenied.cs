// Mirrors Bulwark lib/outgoing/classification-raw.ts.
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Compose;

/// <summary>The sending policy could not authorize the supplied raw message.</summary>
public sealed record RawMessagePolicyDenied() : JmapError("The organization policy did not authorize this raw message.");
