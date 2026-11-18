// Mirrors: PORT-PLAN 4.4 (file pickers through the platform; a hidden run answers from FilesPicker queues and never reaches this class). A picker that cannot open answers null: nothing was chosen.

using Barbican.App.Shell;
using Windows.Storage.Pickers;

namespace Barbican.App.Views.Files;

/// <summary>The platform file and folder pickers for uploads.</summary>
public static class FilesSystemPicker
{
    public static async Task<IReadOnlyList<string>?> PickAsync(bool folder, CancellationToken ct)
    {
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            if (folder)
            {
                var fp = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
                fp.FileTypeFilter.Add("*");
                Init(fp, window);
                var dir = await fp.PickSingleFolderAsync().AsTask(ct).ConfigureAwait(true);
                return dir is null ? null : [dir.Path];
            }

            var open = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            open.FileTypeFilter.Add("*");
            Init(open, window);
            var files = await open.PickMultipleFilesAsync().AsTask(ct).ConfigureAwait(true);
            return files is { Count: > 0 } ? [.. files.Select(f => f.Path)] : null;
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
