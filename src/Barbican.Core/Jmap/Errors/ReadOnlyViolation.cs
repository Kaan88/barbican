// Mirrors: PORT-PLAN.md §4.6 (the ReadOnly allow-list of methods); Rust jmap.rs:96 read_only.

namespace Barbican.Core.Jmap.Errors;

/// <summary>A write was attempted on a read-only client (the read-only account).</summary>
public sealed record ReadOnlyViolation(string Method) : JmapError($"Read-only client refused {Method}");
