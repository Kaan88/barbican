// Mirrors: Rust reminders.rs:284 state_path, :286 load_state, :290 save_state, :297 acknowledge, :306 acked, :315 last_check (calendar.json: acknowledged keys and last check per owner); B-CAL-032. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The per-login reminder state kept in the profile's calendar.json (never in the settings).</summary>
public interface IReminderStateStore
{
    Task<IReadOnlySet<string>> AcknowledgedAsync(string owner, CancellationToken ct);

    Task AcknowledgeAsync(string owner, IReadOnlyList<(string Event, long FireMs, string Alert)> keys, long nowMs, CancellationToken ct);

    Task<long?> LastCheckAsync(string owner, CancellationToken ct);

    Task SetLastCheckAsync(string owner, long nowMs, CancellationToken ct);
}
