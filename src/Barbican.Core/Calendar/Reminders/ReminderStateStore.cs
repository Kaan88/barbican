// Mirrors: Rust reminders.rs:284-315. Stub from the M4 plan section 4.1; owner cal-data replaces the bodies (JSON file in IProfilePaths.ConfigDir, written atomically).

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The default <see cref="IReminderStateStore"/>.</summary>
public sealed class ReminderStateStore : IReminderStateStore
{
    public Task<IReadOnlySet<string>> AcknowledgedAsync(string owner, CancellationToken ct) => throw new PortPendingException("ReminderStateStore.AcknowledgedAsync");

    public Task AcknowledgeAsync(string owner, IReadOnlyList<(string Event, long FireMs, string Alert)> keys, long nowMs, CancellationToken ct) => throw new PortPendingException("ReminderStateStore.AcknowledgeAsync");

    public Task<long?> LastCheckAsync(string owner, CancellationToken ct) => throw new PortPendingException("ReminderStateStore.LastCheckAsync");

    public Task SetLastCheckAsync(string owner, long nowMs, CancellationToken ct) => throw new PortPendingException("ReminderStateStore.SetLastCheckAsync");
}
