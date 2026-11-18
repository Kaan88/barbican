// The default ICalendarFilePicker: queued answers (hidden automation runs) first, then the platform functions the App supplies. Owner cal-ui.

using System.Collections.Concurrent;

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>Queued answers, then the platform pickers.</summary>
public sealed class CalendarFilePicker : ICalendarFilePicker
{
    private readonly ConcurrentQueue<IcsFile?> _opens = new();
    private readonly ConcurrentQueue<string?> _saves = new();
    private readonly Func<CancellationToken, Task<IcsFile?>>? _open;
    private readonly Func<string, string, CancellationToken, Task<string?>>? _save;

    public CalendarFilePicker(Func<CancellationToken, Task<IcsFile?>>? open = null, Func<string, string, CancellationToken, Task<string?>>? save = null)
    {
        _open = open;
        _save = save;
    }

    /// <summary>The next open answers <paramref name="file"/> (null: cancelled).</summary>
    public void QueueOpen(IcsFile? file) => _opens.Enqueue(file);

    /// <summary>The next save answers <paramref name="path"/> without writing (null: cancelled); the written text is kept in <see cref="LastSaved"/>.</summary>
    public void QueueSave(string? path) => _saves.Enqueue(path);

    /// <summary>The text of the last queued save, for automation.</summary>
    public string? LastSaved { get; private set; }

    public Task<IcsFile?> OpenIcsAsync(CancellationToken ct) =>
        _opens.TryDequeue(out var file) ? Task.FromResult(file) : _open is null ? Task.FromResult<IcsFile?>(null) : _open(ct);

    public Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct)
    {
        if (_saves.TryDequeue(out var path))
        {
            LastSaved = path is null ? null : text;
            return Task.FromResult(path);
        }

        return _save is null ? Task.FromResult<string?>(null) : _save(fileName, text, ct);
    }
}
