// Mirrors: Rust reminders.rs:284 state_path, :286 load_state, :290 save_state, :297 acknowledge, :306 acked, :315 last_check (calendar.json: acknowledged keys with their fire time and the last check, per owner); B-CAL-032.
// Written atomically (temp file, then replace) so a crash cannot leave half a file; a damaged file reads as empty (every reminder in it is then treated as new, but the stale threshold keeps old ones from showing). Owner cal-data.

using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The default <see cref="IReminderStateStore"/>.</summary>
public sealed class ReminderStateStore(IProfilePaths paths) : IReminderStateStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The state file of the profile.</summary>
    public string Path { get; } = System.IO.Path.Combine(paths.ConfigDir, "calendar.json");

    public async Task<IReadOnlySet<string>> AcknowledgedAsync(string owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var st = await LoadAsync(ct).ConfigureAwait(false);
            return st["acked"] is JsonObject all && all[owner] is JsonObject mine ? mine.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task AcknowledgeAsync(string owner, IReadOnlyList<(string Event, long FireMs, string Alert)> keys, long nowMs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(keys);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var st = await LoadAsync(ct).ConfigureAwait(false);
            var all = st["acked"] as JsonObject ?? [];
            var mine = all[owner] as JsonObject ?? [];
            foreach (var (ev, fire, alert) in keys)
            {
                mine[ReminderRules.AlertKey(ev, alert, fire)] = fire;
            }

            // entries older than the missed window can never be listed again: drop them
            foreach (var old in mine.Where(kv => kv.Value.AsLong() is not { } f || f <= nowMs - ReminderRules.MissedWindowMs - 86_400_000).Select(kv => kv.Key).ToList())
            {
                _ = mine.Remove(old);
            }

            all[owner] = mine;
            st["acked"] = all;
            await SaveAsync(st, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<long?> LastCheckAsync(string owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var st = await LoadAsync(ct).ConfigureAwait(false);
            return st["lastCheck"] is JsonObject all ? all[owner].AsLong() : null;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task SetLastCheckAsync(string owner, long nowMs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var st = await LoadAsync(ct).ConfigureAwait(false);
            var all = st["lastCheck"] as JsonObject ?? [];
            all[owner] = nowMs;
            st["lastCheck"] = all;
            await SaveAsync(st, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<JsonObject> LoadAsync(CancellationToken ct)
    {
        try
        {
            return File.Exists(Path) && JsonNode.Parse(await File.ReadAllTextAsync(Path, ct).ConfigureAwait(false)) is JsonObject o ? o : [];
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task SaveAsync(JsonObject state, CancellationToken ct)
    {
        try
        {
            _ = Directory.CreateDirectory(paths.ConfigDir);
            var tmp = Path + ".tmp";
            await File.WriteAllTextAsync(tmp, state.ToJsonString(), ct).ConfigureAwait(false);
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a state that cannot be written costs a repeated reminder after a restart, never a crash
        }
    }
}
