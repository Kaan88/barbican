// Mirrors: Rust cal_extra.rs import and export file dialogs (rfd open .ics, save .ics), PORT-PLAN section 4.4 (pickers through the platform), G-004 (the import reads a file the picker returns, at most 5 MB + 1 so the card can refuse an oversize one).
// The platform pickers of WinUI (Uno implements the same API on Skia); on Windows a picker needs the window handle. A hidden automation run never reaches this class: its picks are queued answers (CalendarFilePicker.QueueOpen/QueueSave). A picker that cannot open answers null: nothing was chosen, nothing is written.

using System.Text;
using Barbican.App.Shell;
using Barbican.Presentation.Calendar.Editors.Dialogs;
using Windows.Storage.Pickers;

namespace Barbican.App.Views.Calendar.Editors;

/// <summary>Asks the user for an .ics to import and where to save an event as one.</summary>
internal static class CalendarSystemPicker
{
    /// <summary>The biggest file the import card accepts is 5 MB; one byte more lets it say so instead of reading a whole huge file.</summary>
    private const int MaxReadBytes = ImportViewModel.MaxBytes + 1;

    public static async Task<IcsFile?> OpenIcsAsync(CancellationToken ct)
    {
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            foreach (var ext in new[] { ".ics", ".ical", ".ifb" })
            {
                picker.FileTypeFilter.Add(ext);
            }

            Init(picker, window);
            var file = await picker.PickSingleFileAsync().AsTask(ct).ConfigureAwait(true);
            return file is null ? null : new IcsFile(file.Name, await ReadCappedAsync(file.Path, MaxReadBytes, ct).ConfigureAwait(true));
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static async Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(text);
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(fileName) };
            picker.FileTypeChoices.Add("iCalendar", [".ics"]);
            Init(picker, window);
            var file = await picker.PickSaveFileAsync().AsTask(ct).ConfigureAwait(true);
            if (file is null)
            {
                return null;
            }

            await File.WriteAllTextAsync(file.Path, text, new UTF8Encoding(false), ct).ConfigureAwait(true);
            return file.Path;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // at most <cap> bytes of a file, never the whole of a huge one
    private static async Task<byte[]> ReadCappedAsync(string path, int cap, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var buffer = new byte[(int)Math.Min(stream.Length, cap)];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(true);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static void Init(object picker, Window window)
    {
#if WINDOWS
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
#endif
    }
}
