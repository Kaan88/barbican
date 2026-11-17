// Mirrors: Rust compose.rs:246 resolve_reply_identity return value. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Jmap.Submission;

namespace Barbican.Core.Compose;

/// <summary>The identity a reply is sent through (index into the identity list) and the From override that rewrites the header From, when there is one.</summary>
public sealed record ReplyIdentityChoice(int Index, FromOverride? Override);
