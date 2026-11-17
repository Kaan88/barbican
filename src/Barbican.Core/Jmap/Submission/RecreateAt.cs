// Mirrors: Rust jmap.rs:1449 RescheduleFallback::Recreate. Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>Recreate the submission for the old release time (Unix ms).</summary>
public sealed record RecreateAt(long AtMs) : RescheduleFallback;
