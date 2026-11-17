// Mirrors: Rust jmap.rs:1570 PushEvent::Ping. Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Push;

/// <summary>A keep-alive (<c>event: ping</c>).</summary>
public sealed record PushPing : PushEvent;
