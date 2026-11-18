// Mirrors: Bulwark components/email/reader-attachments.tsx and components/files/file-preview-modal.tsx (image, text and .eml preview); D-M5-15 (no pdf, audio or video in the app); I-228. The card of the dialog host holds it (IDialogContent). Owner files-ui.

using Barbican.Presentation.Files;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Reader;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Barbican.App.Views.Dialogs;

/// <summary>Draws <see cref="AttachmentPreviewDialogRequest"/>: the picture, or the text, and a Save button.</summary>
public sealed class AttachmentPreviewDialogContent : IDialogContent
{
    /// <inheritdoc />
    public bool CanDraw(object request) => request is AttachmentPreviewDialogRequest;

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var request = (AttachmentPreviewDialogRequest)dialog.Request;
        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        UIElement shown;
        if (request.Kind == FilesPreviewKind.Image)
        {
            var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 420, MaxWidth = 640 };
            _ = LoadAsync(image, request.Bytes);
            shown = image;
        }
        else
        {
            var text = System.Text.Encoding.UTF8.GetString(request.Bytes);
            var box = new TextBox { Text = request.Kind == FilesPreviewKind.Eml ? EmlText(text) : text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12, Height = 320 };
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            shown = box;
        }

        AutomationProperties.SetAutomationId(shown, "attachment.preview.body");
        panel.Children.Add(shown);
        var save = new Button { Content = "Save as…", Style = (Style)Application.Current.Resources["Button.Action"], HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(save, "attachment.preview.save");
        save.Click += (_, _) => _ = request.Save(CancellationToken.None);
        panel.Children.Add(save);
        return panel;
    }

    private static async Task LoadAsync(Image image, byte[] bytes)
    {
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await System.IO.WindowsRuntimeStreamExtensions.AsStreamForWrite(stream).WriteAsync(bytes).ConfigureAwait(true);
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or IOException)
        {
            // a picture the decoder refuses stays blank; Save as still has the file
        }
    }

    // the header lines a reader wants, then the text (the raw file is never rendered as HTML here)
    private static string EmlText(string raw)
    {
        var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = split < 0 ? raw : raw[..split];
        var body = split < 0 ? string.Empty : raw[(split + 4)..];
        var lines = head.Split("\r\n").Where(l => l.StartsWith("From:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("To:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Date:", StringComparison.OrdinalIgnoreCase));
        return string.Join("\r\n", lines) + "\r\n\r\n" + body;
    }
}
