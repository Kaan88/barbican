// Mirrors: Bulwark lib/undo-history.ts:20-65 (10 entries), mail-app.tsx:1545 (z and Ctrl+Z); M2 plan section 4.3. Contract from stage F of M2; owner actions.

using Barbican.Core.Common;

namespace Barbican.Presentation.Actions;

/// <summary>The undo history: the last 10 undoable actions.</summary>
public interface IUndoService
{
    UndoEntry Record(string label, Func<CancellationToken, Task<Result<Unit>>> undo);

    Task<bool> UndoLatestAsync(CancellationToken ct);

    /// <summary>Undoes one entry (the toast's Undo button) and takes it off the history so it runs once; false when it was already undone or dropped.</summary>
    Task<bool> UndoAsync(UndoEntry entry, CancellationToken ct);

    IReadOnlyList<UndoEntry> History { get; }
}
