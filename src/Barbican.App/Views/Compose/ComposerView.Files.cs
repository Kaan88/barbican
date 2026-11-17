// Mirrors: Bulwark components/email/email-composer.tsx :5760 (drop files overlay), :6515 (Attach files), paste of files; I-242, I-253. Files reach the composer by the picker, by a drop on the pane, or by a paste.
// The bytes are read here (the platform's StorageFile) and handed to the view model as AttachmentUpload; nothing here touches the real mouse or keyboard.

using Barbican.App.Shell;
using Barbican.Core.Compose;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Barbican.App.Views.Compose;

#pragma warning disable VSTHRD100 // XAML event handlers return void; a failed attach reports itself with a toast
public sealed partial class ComposerView
{
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_vm is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Attach";
        e.Handled = true;
        DropOverlay.Visibility = Visibility.Visible;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (_vm is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await AttachAsync([.. items.OfType<StorageFile>()]);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The Attach button: the system file picker (never opened by an automation run, which attaches through the compose-attach command).</summary>
    private async void OnAttachClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null || ActiveWindow.Current is not { } window)
        {
            return;
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
#if WINDOWS
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
#endif
            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                await AttachAsync([.. files]);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            // no picker (a bare X11 without a portal): nothing was chosen
        }
    }

    // Ctrl+V with files on the clipboard attaches them; text and pictures go to the field or the editor that has the focus (true when files were taken)
    private bool TryPasteFiles()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.StorageItems))
            {
                return false;
            }

            _ = PasteAsync(content);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task PasteAsync(DataPackageView content)
    {
        var items = await content.GetStorageItemsAsync();
        await AttachAsync([.. items.OfType<StorageFile>()]);
    }

    private async Task AttachAsync(IReadOnlyList<StorageFile> files)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var uploads = new List<AttachmentUpload>();
        foreach (var file in files)
        {
            var buffer = await FileIO.ReadBufferAsync(file);
            var bytes = new byte[buffer.Length];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            uploads.Add(new AttachmentUpload(file.Name, string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType, bytes));
        }

        await vm.AddFilesAsync(uploads);
    }
}
