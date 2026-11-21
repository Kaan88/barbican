#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.batch.ts:128-160 (one entry in the app's undo history per add-on batch; the add-on's undo takes the entry off the history first). Presentation's IUndoService can run an entry
// (taking it off the history so it runs once) but has no "drop without running", so Remove runs the entry in the background: its action is the batch's shared once-only undo, which the add-on's own undo then awaits.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.MailActions;
using Barbican.Presentation.Actions;

namespace Barbican.App.Plugins;

internal sealed class PluginMailUndoHistory(IUndoService undo) : IPluginMailUndoHistory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, UndoEntry> _entries = new(StringComparer.Ordinal);
    private long _counter;

    public string Record(string label, Func<CancellationToken, Task<Result<Unit>>> action)
    {
        var entry = undo.Record(label, action);
        lock (_gate)
        {
            var id = "u" + (++_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _entries[id] = entry;
            return id;
        }
    }

    public bool Remove(string entryId)
    {
        UndoEntry? entry;
        lock (_gate)
        {
            if (!_entries.Remove(entryId, out entry)) return false;
        }

        _ = undo.UndoAsync(entry, CancellationToken.None);
        return true;
    }
}
#endif
