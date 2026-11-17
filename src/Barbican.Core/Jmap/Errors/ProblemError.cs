// Mirrors: RFC 7807 problem details as RFC 8620 §3.6.1 uses them; Rust jmap.rs:112 problem_message.

namespace Barbican.Core.Jmap.Errors;

/// <summary>A request-level error (HTTP status with a problem-details body).</summary>
public sealed record ProblemError(int Status, string Type, string? Detail, string? Limit)
    : JmapError($"{Status} {Type}");
