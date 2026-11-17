// Mirrors: Rust jmap.rs request retry rules (PORT-PLAN.md §4.6): reads retry once, writes never.

namespace Barbican.Core.Jmap.Errors;

/// <summary>The request did not complete (network, timeout, server trouble).</summary>
public sealed record TransportError(string Message, bool Retryable) : JmapError(Message);
