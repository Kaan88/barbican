// Mirrors: Rust src/rte/mod.rs pick_image :201 (png, jpg, jpeg, gif, webp, bmp); Shell/SystemFilePicker.cs (the window handle on Windows). A hidden automation run never reaches it.

using Barbican.App.Shell;
using Barbican.Presentation.Editor;
using Windows.Storage.Pickers;

namespace Barbican.App.Views.Editor;

/// <summary>Asks the user for pictures to insert.</summary>
public static class EditorFilePicker
{
    public static async Task<IReadOnlyList<ClipboardFile>> PickImagesAsync()
    {
        if (ActiveWindow.Current is not { } window)
        {
            return [];
        }

        try
        {
            var open = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" })
            {
                open.FileTypeFilter.Add(ext);
            }

#if WINDOWS
            WinRT.Interop.InitializeWithWindow.Initialize(open, WinRT.Interop.WindowNative.GetWindowHandle(window));
#endif
            var files = await open.PickMultipleFilesAsync();
            return files is { Count: > 0 } ? await OsClipboardBackend.ReadFilesAsync([.. files]) : [];
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }
}
