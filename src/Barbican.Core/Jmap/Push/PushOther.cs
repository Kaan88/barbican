// Mirrors: Rust jmap.rs:1570 PushEvent::Other (Stalwart's calendarAlert, ...). Stub from F2; owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Push;

/// <summary>Any other named event.</summary>
public sealed record PushOther(string Name, JsonNode? Data) : PushEvent;
