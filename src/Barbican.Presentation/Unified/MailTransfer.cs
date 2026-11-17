// Mirrors: Rust mailimport.rs:281 import_files and :381 summary (the toast after an import), Bulwark components/mail-import (progress "n
// messages", cancel), lib/mail-export/ (a folder saved as mbox or .eml files). M2 plan 6.5: MENU-FOLDER "Import messages (.eml, .mbox)…",
// Ctrl+O and folder export call these; spec I-065 (Import/Export part), G-046. Owner unified.
// One transfer at a time; Progress is raised on the UI thread (the dispatcher) so the progress row only changes text, never blanks.

using Barbican.Core.Common;
using Barbican.Core.Mail.Import;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Unified;

/// <summary>Runs an import into a folder or an export of a folder, with progress and cancel.</summary>
public sealed partial class MailTransfer : ObservableObject
{
    private readonly IMailImport _import;
    private readonly MailExportService _export;
    private readonly TransferPicks _picks;
    private readonly IDispatcher _dispatcher;
    private CancellationTokenSource? _running;

    /// <summary>True while a transfer runs.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Messages done so far in the running transfer.</summary>
    [ObservableProperty]
    private int _done;

    /// <summary>The last transfer's summary ("Imported 2 messages, 1 already there.").</summary>
    [ObservableProperty]
    private string _summary = "";

    public MailTransfer(IMailImport import, MailExportService export, TransferPicks picks, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _import = import;
        _export = export;
        _picks = picks;
        _dispatcher = dispatcher;
    }

    /// <summary>Asks for .eml/.mbox files and imports them into <paramref name="target"/> (MENU-FOLDER "Import messages…", Ctrl+O).</summary>
    public async Task<Result<ImportOutcome>?> ImportAsync(MailboxRef target, CancellationToken ct)
    {
        var paths = await _picks.OpenAsync(ct).ConfigureAwait(true);
        return paths is null ? null : await ImportFilesAsync(target, paths, ct).ConfigureAwait(true);
    }

    /// <summary>Imports the given files into <paramref name="target"/>.</summary>
    public async Task<Result<ImportOutcome>?> ImportFilesAsync(MailboxRef target, IReadOnlyList<string> paths, CancellationToken ct)
    {
        using var run = Begin(ct);
        if (run is null)
        {
            return null;
        }

        try
        {
            var r = await _import.ImportFilesAsync(target, paths, new Progress(this), run.Token).ConfigureAwait(true);
            Finish(r.IsOk ? MboxFormat.Summary(r.Value!.Imported, r.Value.Already, r.Value.Errors.Count, r.Value.Errors.Count > 0 ? r.Value.Errors[0] : null) : r.Error!.Message);
            return r;
        }
        catch (OperationCanceledException)
        {
            Finish($"Import stopped after {Done} messages.");
            return null;
        }
    }

    /// <summary>Saves <paramref name="folder"/> as one mbox file, or as .eml files in a folder.</summary>
    public async Task<Result<int>?> ExportAsync(MailboxRef folder, string folderName, bool asMbox, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        var path = await _picks.SaveAsync(asMbox ? folderName + ".mbox" : folderName, ct).ConfigureAwait(true);
        if (path is null)
        {
            return null;
        }

        using var run = Begin(ct);
        if (run is null)
        {
            return null;
        }

        try
        {
            Result<int> r;
            if (asMbox)
            {
                var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using (file.ConfigureAwait(true))
                {
                    r = await _export.ExportMboxAsync(folder, file, new Progress(this), run.Token).ConfigureAwait(true);
                }
            }
            else
            {
                r = await _export.ExportEmlAsync(folder, path, new Progress(this), run.Token).ConfigureAwait(true);
            }

            Finish(r.IsOk ? $"Saved {r.Value} messages." : r.Error!.Message);
            return r;
        }
        catch (OperationCanceledException)
        {
            Finish($"Saving stopped after {Done} messages.");
            return null;
        }
    }

    /// <summary>Stops the running transfer between messages.</summary>
    public void Cancel() => _running?.Cancel();

    private CancellationTokenSource? Begin(CancellationToken ct)
    {
        if (IsRunning)
        {
            return null;
        }

        _running = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Done = 0;
        IsRunning = true;
        return _running;
    }

    private void Finish(string summary)
    {
        Summary = summary;
        IsRunning = false;
        _running = null;
    }

    private sealed class Progress(MailTransfer owner) : IProgress<int>
    {
        public void Report(int value) => owner._dispatcher.Post(() => owner.Done = Math.Max(owner.Done, value));
    }
}
