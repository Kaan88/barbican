#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.batch.ts:128-160 (one entry in the app's undo history per add-on batch; the add-on's undo takes the entry off the history first). Presentation's IUndoService can run an entry
// (taking it off the history so it runs once) but has no "drop without running", so Remove runs the entry in the background: its action is the batch's shared once-only undo, which the add-on's own undo then awaits.
// Prunes: once the add-on's undo token has expired (PluginUndoTokens.Lifetime) its entry can never be removed again, so Record and Remove drop entries past that age and the oldest beyond a fixed cap.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.MailActions;
using Barbican.Presentation.Actions;

namespace Barbican.App.Plugins;

internal sealed class PluginMailUndoHistory(IUndoService undo, TimeProvider time) : IPluginMailUndoHistory
{
    private const int MaxEntries = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (UndoEntry Entry, DateTimeOffset Recorded)> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private long _counter;

    public string Record(string label, Func<CancellationToken, Task<Result<Unit>>> action)
    {
        var entry = undo.Record(label, action);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Prune(now);
            var id = "u" + (++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _entries[id] = (entry, now);
            return id;
        }
    }

    public bool Remove(string entryId)
    {
        UndoEntry entry;
        lock (_gate)
        {
            Prune(_time.GetUtcNow());
            if (!_entries.Remove(entryId, out var stored)) return false;
            entry = stored.Entry;
        }

        _ = undo.UndoAsync(entry, CancellationToken.None);
        return true;
    }

    private void Prune(DateTimeOffset now)
    {
        var cutoff = now - PluginUndoTokens.Lifetime;
        foreach (var id in _entries.Where(pair => pair.Value.Recorded <= cutoff).Select(pair => pair.Key).ToArray())
        {
            _ = _entries.Remove(id);
        }

        while (_entries.Count >= MaxEntries)
        {
            _ = _entries.Remove(_entries.MinBy(pair => pair.Value.Recorded).Key);
        }
    }
}
#endif
