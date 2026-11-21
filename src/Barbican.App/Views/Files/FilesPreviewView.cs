// Mirrors: Bulwark components/files/file-preview-modal.tsx, image-preview-modal.tsx (zoom, rotate, previous and next), eml-preview.tsx, lib/file-preview.ts; G-013; D-M5-15 (pdf, audio and video open outside the app, so this modal never shows them).
// Bulwark (WP-14): preview-shell.tsx:121-144 (a full-screen bg-black/80 overlay; the header is bg-background/90 with a border-b, the name text-sm medium, icon buttons), zoomable-image.tsx:286 (zoom eases 200 ms: the transform here snaps, no easing). An in-window overlay (ADR 0007), not a window. The bytes come from IFileTransfer.ReadAsync under a cap (files-data). The .eml view shows the header lines and the body text of the file; the reader's own MIME parser has no public entry for a raw file, so no HTML or attachments here (D-M5-D06).

using Barbican.App.Shell;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Barbican.App.Theme;
using Barbican.Core.Files;
using Barbican.Presentation.Files;
using Windows.System;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass; splitting it would only pass the same locals around
namespace Barbican.App.Views.Files;

/// <summary>The preview modal of an image, a text file or an .eml.</summary>
public sealed class FilesPreviewView : UserControl
{
    private const int Cap = 12 * 1024 * 1024;
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly Grid _card = new();
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _body = new();
    private readonly Grid _dim = new() { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)) };
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Grid _bar = new() { Padding = new Thickness(16, 10, 10, 10), ColumnSpacing = 6, BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private double _zoom = 1;
    private int _rotation;
    private FileEntry? _current;
    private int _version;

    public FilesPreviewView()
    {
        Visibility = Visibility.Collapsed;
        IsTabStop = true;
        var dim = _dim;
        dim.Tapped += (_, _) => Close();
        _card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _card.Tapped += (_, e) => e.Handled = true;
        var bar = _bar;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 6; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        bar.Children.Add(_title);
        Tool(bar, 1, "‹", "files.preview.prev", () => Step(-1));
        Tool(bar, 2, "›", "files.preview.next", () => Step(1));
        Tool(bar, 3, "−", "files.preview.zoom-out", () => Zoom(0.8));
        Tool(bar, 4, "+", "files.preview.zoom-in", () => Zoom(1.25));
        Tool(bar, 5, "⟳", "files.preview.rotate", () =>
        {
            _rotation = (_rotation + 90) % 360;
            ApplyImage();
        });
        Tool(bar, 6, "✕", "files.preview.close", Close);
        _card.Children.Add(bar);
        Grid.SetRow(_body, 1);
        _card.Children.Add(_body);
        dim.Children.Add(_card);
        Content = dim;
        AutomationProperties.SetAutomationId(this, "files.preview");
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Escape:
                    Close();
                    break;
                case VirtualKey.Left:
                    Step(-1);
                    break;
                case VirtualKey.Right:
                    Step(1);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        };
        FilesUi.Actions.PreviewRequested += e => Show(e);
        _look.PropertyChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    private void ApplyLook()
    {
        if (_look.IsBulwark)
        {
            _card.Background = null;
            _dim.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(204, 0, 0, 0)); // bg-black/80
            _card.Margin = new Thickness(0);
            _card.MaxWidth = double.PositiveInfinity;
            _card.CornerRadius = new CornerRadius(0);
            _bar.Background = FilesVisuals.Res("Ground");
            _bar.BorderBrush = FilesVisuals.Res("Line");
            _title.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            return;
        }

        _card.Background = _look.TileBrush;
        _dim.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0));
        _card.Margin = new Thickness(48);
        _card.MaxWidth = 960;
        _card.CornerRadius = new CornerRadius(14);
        _bar.Background = null;
        _bar.BorderBrush = null;
        _title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
    }

    /// <summary>The file on show, or null while closed (automation reads it).</summary>
    public FileEntry? Current => Visibility == Visibility.Visible ? _current : null;

    /// <summary>Opens the preview of a file.</summary>
    public void Show(FileEntry e)
    {
        _current = e;
        _zoom = 1;
        _rotation = 0;
        Visibility = Visibility.Visible;
        _title.Text = e.Name;
        _body.Child = new TextBlock { Text = "Loading…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
        _ = Focus(FocusState.Programmatic);
        FilesUi.Run(() => LoadAsync(e, ++_version));
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _current = null;
    }

    private async Task LoadAsync(FileEntry e, int version)
    {
        var transfer = ViewHost.Find<IFileTransfer>();
        if (transfer is null)
        {
            return;
        }

        var r = await transfer.ReadAsync(_vm.AccountKey, e.Key, Cap, CancellationToken.None).ConfigureAwait(true);
        if (version != _version)
        {
            return;
        }

        if (!r.IsOk || r.Value is not { } bytes)
        {
            _body.Child = new TextBlock { Text = "This file could not be loaded.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return;
        }

        switch (FilesPreviewRules.KindOf(e.MediaType, e.Name))
        {
            case FilesPreviewKind.Image:
                var bmp = new BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    await System.IO.WindowsRuntimeStreamExtensions.AsStreamForWrite(ms).WriteAsync(bytes).ConfigureAwait(true);
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }

                _image.Source = bmp;
                _body.Child = new ScrollViewer { Content = _image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                ApplyImage();
                break;
            case FilesPreviewKind.Eml:
                _body.Child = Text(Eml(System.Text.Encoding.UTF8.GetString(bytes)));
                break;
            default:
                _body.Child = Text(System.Text.Encoding.UTF8.GetString(bytes));
                break;
        }
    }

    private static string Eml(string raw)
    {
        var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = split < 0 ? raw : raw[..split];
        var body = split < 0 ? string.Empty : raw[(split + 4)..];
        var lines = head.Split("\r\n").Where(l => l.StartsWith("From:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("To:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Date:", StringComparison.OrdinalIgnoreCase));
        return string.Join("\r\n", lines) + "\r\n\r\n" + body;
    }

    // Bulwark: the text sits on a background panel over the dimmed overlay, not over the page behind it
    private UIElement Text(string s)
    {
        var scroll = new ScrollViewer
        {
            Content = new TextBlock { Text = s, FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(20, 8, 20, 20) },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        return _look.IsBulwark ? new Border { Child = scroll, Background = FilesVisuals.Res("Ground"), CornerRadius = new CornerRadius(8), MaxWidth = 960, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Stretch } : scroll;
    }

    private void ApplyImage()
    {
        _image.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _image.RenderTransform = new CompositeTransform { ScaleX = _zoom, ScaleY = _zoom, Rotation = _rotation };
    }

    private void Zoom(double factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.2, 8);
        ApplyImage();
    }

    private void Step(int delta)
    {
        var kinds = _vm.Entries.Where(x => !x.IsDirectory && FilesPreviewRules.KindOf(x.MediaType, x.Name) is FilesPreviewKind.Image or FilesPreviewKind.Text or FilesPreviewKind.Eml).ToList();
        var i = kinds.FindIndex(x => string.Equals(x.Key, _current?.Key, StringComparison.Ordinal));
        if (i >= 0 && kinds.Count > 1)
        {
            Show(kinds[(i + delta + kinds.Count) % kinds.Count]);
        }
    }

    private static void Tool(Grid bar, int col, string glyph, string id, Action click)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Content = glyph, Width = 32, Height = 32, CornerRadius = ViewHost.Get<StyleState>().ButtonRadius };
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => click();
        Grid.SetColumn(b, col);
        bar.Children.Add(b);
    }
}
