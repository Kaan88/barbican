#if WINDOWS
// In-window rendering of add-on supplied PDF bytes; document navigation is not exposed.
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Barbican.App.Views.Plugins;

/// <summary>A bounded, one-page-at-a-time preview of an in-memory PDF.</summary>
public sealed class PluginPdfPreview : UserControl, IDisposable
{
    private const int MaxBytes = 25 * 1024 * 1024;
    private const uint MaxPages = 10000;
    private const double MaxDimension = 1600;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxWidth = 640, MaxHeight = 520 };
    private readonly TextBlock _status = new() { Text = "Loading preview…", TextWrapping = TextWrapping.Wrap };
    private readonly Button _previous = new() { Content = "Previous", IsEnabled = false };
    private readonly Button _next = new() { Content = "Next", IsEnabled = false };
    private InMemoryRandomAccessStream? _source;
    private PdfDocument? _document;
    private CancellationTokenSource? _render;
    private uint _page;
    private bool _disposed;
    private bool _loaded;

    /// <summary>Creates the preview on the UI thread.</summary>
    public PluginPdfPreview()
    {
        AutomationProperties.SetAutomationId(_image, "plugin.pdf.preview");
        AutomationProperties.SetAutomationId(_status, "plugin.pdf.status");
        AutomationProperties.SetAutomationId(_previous, "plugin.pdf.previous");
        AutomationProperties.SetAutomationId(_next, "plugin.pdf.next");
        _previous.Click += (_, _) => Navigate(-1);
        _next.Click += (_, _) => Navigate(1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_previous);
        buttons.Children.Add(_next);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_status);
        panel.Children.Add(new ScrollViewer { Content = _image, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(buttons);
        Content = panel;
    }

    /// <summary>Loads one document. Cancellation and disposal stop pending work.</summary>
    public async Task<bool> LoadAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (_loaded || _disposed) throw new InvalidOperationException("This preview cannot load another document.");
        _loaded = true;
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            _status.Text = "This PDF is empty or too large to preview.";
            return false;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var ct = linked.Token;
        InMemoryRandomAccessStream? source = null;
        try
        {
            source = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(source))
            {
                writer.WriteBytes(bytes);
                _ = await writer.StoreAsync().AsTask(ct).ConfigureAwait(true);
                _ = writer.DetachStream();
            }
            source.Seek(0);
            var document = await PdfDocument.LoadFromStreamAsync(source).AsTask(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (document.PageCount == 0 || document.PageCount > MaxPages)
            {
                _status.Text = "This PDF has no pages or too many pages to preview.";
                return false;
            }
            _source = source;
            source = null;
            _document = document;
            var rendered = await RenderAsync(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            return rendered;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            if (!_disposed) _status.Text = "This PDF could not be previewed.";
            return false;
        }
        finally { source?.Dispose(); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _image.Source = null;
        _document = null;
        _source?.Dispose();
        _source = null;
    }

    private void Navigate(int direction)
    {
        if (_document is null || _disposed || _render is not null) return;
        var next = (long)_page + direction;
        if (next < 0 || next >= _document.PageCount) return;
        _page = (uint)next;
        _ = RenderAsync(_lifetime.Token);
    }

    private async Task<bool> RenderAsync(CancellationToken cancellationToken)
    {
        var current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _render = current;
        var ct = current.Token;
        var document = _document;
        var pageNumber = _page;
        _previous.IsEnabled = false;
        _next.IsEnabled = false;
        _image.Source = null;
        try
        {
            if (document is null) return false;
            using var page = document.GetPage(pageNumber);
            var size = page.Size;
            if (size.Width <= 0 || size.Height <= 0 || !double.IsFinite(size.Width) || !double.IsFinite(size.Height))
                throw new InvalidOperationException("Invalid page size.");
            var scale = Math.Min(1, MaxDimension / Math.Max(size.Width, size.Height));
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Ceiling(size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Ceiling(size.Height * scale)),
            };
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, options).AsTask(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            output.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(output).AsTask(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (_disposed) return false;
            _image.Source = bitmap;
            _status.Text = $"Page {pageNumber + 1} of {document.PageCount}";
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            if (!_disposed && ReferenceEquals(_render, current)) _status.Text = "This page could not be previewed.";
            return false;
        }
        finally
        {
            _render = null;
            if (!_disposed && document is not null)
            {
                _previous.IsEnabled = pageNumber > 0;
                _next.IsEnabled = pageNumber + 1 < document.PageCount;
            }
            current.Dispose();
        }
    }
}
#endif
