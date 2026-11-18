// Mirrors: Rust contacts_x.rs import (rfd open .vcf), export save, D-M5-18 (export goes through the system save picker only; the file name is the suggested one, the picker has the last word); G-012 (a picked file is read up to a cap, a picked image up to the photo cap, so a hostile file cannot fill memory before the parser's own limits answer).
// The platform pickers of WinUI (Uno implements the same API on Skia); on Windows a picker needs the window handle. A hidden automation run never reaches this class: its picks are queued answers (ContactsFilePicker.QueueOpen). A picker that cannot open answers null: nothing was chosen, nothing is written.

using System.Text;
using Barbican.App.Shell;
using Barbican.Core.Contacts;
using Barbican.Presentation.Contacts;
using Windows.Storage.Pickers;

namespace Barbican.App.Views.Contacts;

/// <summary>Asks the user for a .vcf to import, a photo, and where to save an export.</summary>
internal static class ContactsSystemPicker
{
    private const int MaxReadBytes = 20 * 1024 * 1024;

    public static async Task<ContactsPickedFile?> OpenVcardAsync(CancellationToken ct)
    {
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".vcf");
            picker.FileTypeFilter.Add(".vcard");
            Init(picker, window);
            var file = await picker.PickSingleFileAsync().AsTask(ct).ConfigureAwait(true);
            if (file is null)
            {
                return null;
            }

            var bytes = await ReadCappedAsync(file.Path, MaxReadBytes, ct).ConfigureAwait(true);
            return new ContactsPickedFile(file.Name, Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static async Task<ContactsPickedImage?> OpenImageAsync(CancellationToken ct)
    {
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" })
            {
                picker.FileTypeFilter.Add(ext);
            }

            Init(picker, window);
            var file = await picker.PickSingleFileAsync().AsTask(ct).ConfigureAwait(true);
            return file is null ? null : new ContactsPickedImage(file.Name, await ReadCappedAsync(file.Path, ContactPhoto.MaxInputBytes + 1, ct).ConfigureAwait(true));
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static async Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct)
    {
        if (ActiveWindow.Current is not { } window)
        {
            return null;
        }

        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Downloads, SuggestedFileName = Path.GetFileNameWithoutExtension(fileName) };
            picker.FileTypeChoices.Add("vCard", [".vcf"]);
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
