// Mirrors: Rust mailimport.rs:300 (rfd dialog: .eml and .mbox, several files) and the export save dialogs; PORT-PLAN §4.4 (file pickers through the platform service). Owner unified.
// The platform pickers of WinUI (Uno implements the same API on Skia). On Windows the picker needs the window handle. A hidden automation run never reaches this class: its
// picks are queued answers (TransferPicks.Queue). A picker that cannot open (no portal on a bare X11) answers null: nothing was chosen, nothing is written.

using Windows.Storage.Pickers;

namespace Barbican.App.Shell;

/// <summary>Asks the user for files to import and where to save an export.</summary>
public static class SystemFilePicker
{
    /// <summary>
    /// The <see cref="Barbican.Presentation.Unified.TransferPicks"/> platform function: open (.eml, .mbox, several) when <paramref name="save"/> is false; otherwise a save
    /// file for a name ending in ".mbox" and a folder for the .eml export.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> PickAsync(bool save, string suggested, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(suggested);
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            if (!save)
            {
                var open = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
                open.FileTypeFilter.Add(".eml");
                open.FileTypeFilter.Add(".mbox");
                Init(open, window);
                var files = await open.PickMultipleFilesAsync().AsTask(ct).ConfigureAwait(true);
                return files is { Count: > 0 } ? [.. files.Select(f => f.Path)] : null;
            }

            if (suggested.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = Path.GetFileNameWithoutExtension(suggested) };
                picker.FileTypeChoices.Add("mbox", [".mbox"]);
                Init(picker, window);
                var file = await picker.PickSaveFileAsync().AsTask(ct).ConfigureAwait(true);
                return file is null ? null : [file.Path];
            }

            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            folderPicker.FileTypeFilter.Add("*");
            Init(folderPicker, window);
            var folder = await folderPicker.PickSingleFolderAsync().AsTask(ct).ConfigureAwait(true);
            return folder is null ? null : [Path.Combine(folder.Path, suggested)];
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static void Init(object picker, Window window)
    {
#if WINDOWS
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
#endif
    }
}
