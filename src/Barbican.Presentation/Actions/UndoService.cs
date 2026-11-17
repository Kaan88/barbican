// Mirrors: Bulwark lib/undo-history.ts:20-65 (the 10 newest undoable actions, newest undone first; an entry runs once, whether by Ctrl+Z, z or the
// toast's Undo button; a failed undo says so), mail-app.tsx:1545 (z and Ctrl+Z). Owner actions.

using Barbican.Core.Common;

namespace Barbican.Presentation.Actions;

/// <summary>The undo history of mail actions.</summary>
public sealed class UndoService(IToastService toasts) : IUndoService
{
    /// <summary>How many actions are kept (undo-history.ts MAX_UNDO_HISTORY).</summary>
    public const int MaxEntries = 10;

    private readonly Lock _lock = new();
    private readonly List<UndoEntry> _entries = [];

    /// <inheritdoc />
    public IReadOnlyList<UndoEntry> History
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public UndoEntry Record(string label, Func<CancellationToken, Task<Result<Unit>>> undo)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(undo);
        var entry = new UndoEntry(label, undo);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }

        return entry;
    }

    /// <inheritdoc />
    public async Task<bool> UndoLatestAsync(CancellationToken ct)
    {
        UndoEntry? latest;
        lock (_lock)
        {
            latest = _entries.Count > 0 ? _entries[^1] : null;
        }

        return latest is not null && await UndoAsync(latest, ct).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> UndoAsync(UndoEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            if (!_entries.Remove(entry))
            {
                return false;
            }
        }

        var result = await entry.Undo(ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            toasts.Show(new ToastRequest(result.Error?.Message ?? "Could not undo that.", IsError: true));
        }

        return true;
    }

    /// <summary>Forgets everything (signing out).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
