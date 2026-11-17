// Mirrors: Rust jmap.rs:1570 PushEvent; RFC 8620 §7.3 (EventSource). Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Push;

/// <summary>An event of the push stream (closed hierarchy: the sealed records in this folder).</summary>
public abstract record PushEvent
{
    private protected PushEvent()
    {
    }
}
