// Mirrors: M2 plan 6.5 ("file pickers through the platform service, queued pick answers in automation"); Rust mailimport.rs:300 (the
// rfd dialog: .eml and .mbox, several files). spec I-065 (Import/Export part). Owner unified.
// A queued answer is used first (hidden automation runs never show a dialog); otherwise the platform picker the App registers is asked.
// Until the App has a picker (open item, M2 Fable), an unanswered pick returns null and the command reports that nothing was chosen.

using System.Collections.Concurrent;

namespace Barbican.Presentation.Unified;

/// <summary>Where import reads from and export writes to.</summary>
public sealed class TransferPicks
{
    private readonly ConcurrentQueue<IReadOnlyList<string>> _queued = new();
    private readonly Func<bool, string, CancellationToken, Task<IReadOnlyList<string>?>>? _platform;
    private readonly Func<string, CancellationToken, Task<string?>>? _saveFile;

    /// <param name="platform">The platform picker: (save, suggested name) to the chosen paths, or null when cancelled; null when there is none.</param>
    /// <param name="saveFile">The platform save-file dialog for one file (an add-on's ui.downloadFile): the suggested name to the chosen path, or null when cancelled.</param>
    public TransferPicks(Func<bool, string, CancellationToken, Task<IReadOnlyList<string>?>>? platform = null, Func<string, CancellationToken, Task<string?>>? saveFile = null)
    {
        _platform = platform;
        _saveFile = saveFile;
    }

    /// <summary>Where to save one file (a save dialog suggesting <paramref name="suggested"/>), or null when cancelled; a queued answer is used first, so a hidden run never shows a dialog.</summary>
    public Task<string?> SaveFileAsync(string suggested, CancellationToken ct)
    {
        if (_queued.TryDequeue(out var answer))
        {
            return Task.FromResult(answer.Count > 0 ? answer[0] : null);
        }

        return _saveFile is null ? Task.FromResult<string?>(null) : _saveFile(suggested, ct);
    }

    /// <summary>The next pick answers <paramref name="paths"/> (hidden automation runs).</summary>
    public void Queue(params string[] paths) => _queued.Enqueue(paths);

    /// <summary>Files to import (.eml, .mbox), or null when none were chosen.</summary>
    public Task<IReadOnlyList<string>?> OpenAsync(CancellationToken ct) => PickAsync(save: false, "", ct);

    /// <summary>Where to save an export (a file for mbox, a folder for .eml), or null when cancelled.</summary>
    public async Task<string?> SaveAsync(string suggested, CancellationToken ct) =>
        await PickAsync(save: true, suggested, ct).ConfigureAwait(false) is [var first, ..] ? first : null;

    private Task<IReadOnlyList<string>?> PickAsync(bool save, string suggested, CancellationToken ct)
    {
        if (_queued.TryDequeue(out var answer))
        {
            return Task.FromResult<IReadOnlyList<string>?>(answer.Count > 0 ? answer : null);
        }

        return _platform is null ? Task.FromResult<IReadOnlyList<string>?>(null) : _platform(save, suggested, ct);
    }
}
