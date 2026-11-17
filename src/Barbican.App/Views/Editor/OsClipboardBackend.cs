// Mirrors: Rust src/rte/mod.rs copy :418 (set_html with a plain alternative), paste :440 (file list, html, bitmap, text); Windows.ApplicationModel.DataTransfer (CF_HTML through HtmlFormatHelper).

using System.Runtime.InteropServices.WindowsRuntime;
using Barbican.Presentation.Editor;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Barbican.App.Views.Editor;

/// <summary>The operating system's clipboard.</summary>
public sealed class OsClipboardBackend : IEditorClipboardBackend
{
    public async Task<IEditorClipboard> SnapshotAsync()
    {
        var snapshot = new Snapshot();
        try
        {
            var view = Clipboard.GetContent();
            if (view.Contains(StandardDataFormats.Html))
            {
                snapshot.Html = await view.GetHtmlFormatAsync();
            }

            if (view.Contains(StandardDataFormats.Text))
            {
                snapshot.Text = await view.GetTextAsync();
            }

            if (view.Contains(StandardDataFormats.StorageItems))
            {
                snapshot.Files = await ReadFilesAsync(await view.GetStorageItemsAsync());
            }

            if (view.Contains(StandardDataFormats.Bitmap))
            {
                var stream = await (await view.GetBitmapAsync()).OpenReadAsync();
                var buffer = new byte[stream.Size];
                _ = await stream.ReadAsync(buffer.AsBuffer(), (uint)buffer.Length, Windows.Storage.Streams.InputStreamOptions.None);
                snapshot.Image = new ClipboardFile("pasted-image.png", "image/png", buffer);
            }
        }
#pragma warning disable CA1031 // the clipboard can be locked by another process or hold a format that fails to convert: a paste of what could be read is better than none
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        return snapshot;
    }

    public void Write(string html, string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(html));
            Clipboard.SetContent(package);
        }
#pragma warning disable CA1031 // a locked clipboard is not an error the user can act on
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Reads the pictures among dropped or copied files (also used by the drop handler).</summary>
    public static async Task<IReadOnlyList<ClipboardFile>> ReadFilesAsync(IReadOnlyList<IStorageItem> items)
    {
        var files = new List<ClipboardFile>();
        foreach (var item in items.OfType<IStorageFile>())
        {
            var mime = MimeOf(item.Name);
            if (!EditorClipboardActions.IsPictureMime(mime))
            {
                continue;
            }

            var buffer = await FileIO.ReadBufferAsync(item);
            files.Add(new ClipboardFile(item.Name, mime, buffer.ToArray()));
        }

        return files;
    }

    private static string MimeOf(string name) => Path.GetExtension(name).ToUpperInvariant() switch
    {
        ".PNG" => "image/png",
        ".JPG" or ".JPEG" => "image/jpeg",
        ".GIF" => "image/gif",
        ".WEBP" => "image/webp",
        ".BMP" => "image/bmp",
        ".SVG" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}
