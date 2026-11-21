// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.batch.ts:128-135 (one entry in the app's undo history per email.batch: Ctrl+Z and the add-on's undo take back the same thing, once)
// and lib/undo-history.ts:20-65 (recordUndo, runUndo). Host seam: the app's undo history; Core cannot reference Presentation's IUndoService.
using Barbican.Core.Common;

namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>Where a batch's undo is recorded so the user's own Undo reaches it as well.</summary>
public interface IPluginMailUndoHistory
{
    /// <summary>Records <paramref name="undo"/> under <paramref name="label"/> and returns the id of the entry.</summary>
    string Record(string label, Func<CancellationToken, Task<Result<Unit>>> undo);

    /// <summary>Takes the entry off the history (the add-on's own undo ran it); false when it is already gone.</summary>
    bool Remove(string entryId);
}
