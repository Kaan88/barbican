// Mirrors: Rust jmap.rs:1570 PushEvent::State; RFC 8620 §7.1 (StateChange). Stub from F2; owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Push;

/// <summary>A StateChange object.</summary>
public sealed record PushState(JsonNode Change) : PushEvent;
