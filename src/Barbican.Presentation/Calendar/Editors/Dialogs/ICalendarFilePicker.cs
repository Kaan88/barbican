// Mirrors: Rust cal_extra.rs import and export file dialogs (rfd), PORT-PLAN section 4.4 (file pickers through the platform service). Presentation has no file IO, so the platform side reads and writes the file.
// Contract of cal-ui; the App implements it with the shell's pickers, a hidden automation run answers from a queue.

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>Picks an .ics file to read and a place to save one.</summary>
public interface ICalendarFilePicker
{
    /// <summary>The chosen .ics file (name and bytes, at most 5 MB are read), or null when cancelled.</summary>
    Task<IcsFile?> OpenIcsAsync(CancellationToken ct);

    /// <summary>Saves <paramref name="text"/> under a name the user chooses (suggested <paramref name="fileName"/>); the saved path, or null when cancelled.</summary>
    Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct);
}
