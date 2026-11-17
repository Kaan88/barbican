// Mirrors: Rust jmap.rs:1465 scheduled_hold (the Err strings), M1-plan §4.1 (added by F2: the Rust R<u64> error is a string).

using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Jmap;

/// <summary>Scheduled send refused (unsupported, in the past, or beyond the server's limit).</summary>
public sealed record ScheduleError(string Message) : JmapError(Message);
