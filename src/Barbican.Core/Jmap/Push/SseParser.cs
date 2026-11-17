// Mirrors: Rust jmap.rs:1583 SseParser; WHATWG server-sent events; RFC 8620 §7.3; Stalwart crates/jmap/src/api/event_source.rs.
// Stub from F2; owner jmap.

using Barbican.Core.Common;

// CA1822: instance members of a stub that no stream has implemented yet; each gains state when its stream lands.
#pragma warning disable CA1822

namespace Barbican.Core.Jmap.Push;

/// <summary>Line-by-line text/event-stream parser.</summary>
public sealed class SseParser
{
    /// <summary>Feeds one line (with or without a trailing CR); returns an event when a blank line completes one.</summary>
    public PushEvent? Line(string line) => throw new PortPendingException("SseParser.Line: jmap stream");
}
