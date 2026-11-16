using Microsoft.UI;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// The spike's fixed layout, in DIPs from the root's top-left corner:
/// a marker square (finds the root in a capture), a native header with a swatch whose colour names
/// the message the header shows, and the body view (the WebView2 running the shell) below it.
/// </summary>
internal sealed class SpikeRoot : Grid
{
    public const int WindowWidth = 1000;
    public const int WindowHeight = 720;
    public const double MarkerSize = 8;
    public const double HeaderHeight = 64;
    public const double BodyLeft = 16;
    public const double BodyTop = 80;
    public const double BodyWidth = 800;
    public const double BodyHeight = 540;

    public static readonly Color Marker = Color.FromArgb(255, 0x12, 0xFE, 0x34);
    public static readonly Color TileColor = Color.FromArgb(255, 0x14, 0x14, 0x14);

    public SpikeRoot()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Width = BodyLeft + BodyWidth + 120;
        Height = BodyTop + BodyHeight + 40;
        Background = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x0A, 0x0A));

        var canvas = new Canvas();
        Children.Add(canvas);

        var header = new Border { Width = BodyWidth, Height = HeaderHeight - 8, Background = new SolidColorBrush(TileColor), CornerRadius = new CornerRadius(12) };
        Canvas.SetLeft(header, BodyLeft);
        Canvas.SetTop(header, 4);
        canvas.Children.Add(header);

        Swatch = new Border { Width = 40, Height = 40, Background = new SolidColorBrush(TileColor) };
        Canvas.SetLeft(Swatch, BodyLeft + 12);
        Canvas.SetTop(Swatch, 12);
        canvas.Children.Add(Swatch);

        HeaderText = new TextBlock { Foreground = new SolidColorBrush(Colors.White), FontSize = 16, Text = "(no message)" };
        Canvas.SetLeft(HeaderText, BodyLeft + 68);
        Canvas.SetTop(HeaderText, 22);
        canvas.Children.Add(HeaderText);

        BodyHost = new Grid { Width = BodyWidth, Height = BodyHeight, Background = new SolidColorBrush(TileColor) };
        Canvas.SetLeft(BodyHost, BodyLeft);
        Canvas.SetTop(BodyHost, BodyTop);
        canvas.Children.Add(BodyHost);

        Body = new WebView2 { DefaultBackgroundColor = TileColor };
        BodyHost.Children.Add(Body);

        // Linux overlay strategy: the stand-in shown in the body's place while the native view is hidden.
        Standin = new Border { Visibility = Visibility.Collapsed };
        BodyHost.Children.Add(Standin);

        Overlay = new Grid { Visibility = Visibility.Collapsed, Width = Width, Height = Height };
        canvas.Children.Add(Overlay);

        // Last, so nothing covers it.
        var marker = new Rectangle { Width = MarkerSize, Height = MarkerSize, Fill = new SolidColorBrush(Marker) };
        canvas.Children.Add(marker);
    }

    public Border Swatch { get; }

    public TextBlock HeaderText { get; }

    public Grid BodyHost { get; }

    public WebView2 Body { get; }

    public Border Standin { get; }

    public Grid Overlay { get; }

    public static Rect BodyRect => new(BodyLeft, BodyTop, BodyWidth, BodyHeight);

    public static Rect SwatchRect => new(BodyLeft + 12, 12, 40, 40);
}
