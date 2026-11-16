#if !WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Barbican.Spike.BodyShell.Capture;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// Linux overlay strategy (PORT-PLAN.md §3.6, IBodyOverlayStrategy.Linux): WebKitGTK is a separate
/// X window above the Uno window, so before an overlay opens the body is captured (XGetImage), the
/// capture is shown in its place and the native view is hidden; afterwards the view comes back.
/// </summary>
internal sealed partial class Scenario
{
    private async Task LinuxStandinPhaseAsync()
    {
        await ShowAsync(MailDocument.Solid(Magenta, "magenta"), Magenta, false, ShellHost.Transport.Script, A, "magenta");
        await Task.Delay(300);
        var shot = _capture!.Grab();
        var origin = shot is null ? null : FrameLocator.Find(shot, SpikeRoot.Marker, SpikeRoot.MarkerSize);
        if (shot is null || origin is not { } o)
        {
            Check("Linux stand-in: capture of the body", false, "no frame or marker");
            return;
        }

        var x0 = o.X + (int)(SpikeRoot.BodyLeft * o.Scale);
        var y0 = o.Y + (int)(SpikeRoot.BodyTop * o.Scale);
        var w = (int)(SpikeRoot.BodyWidth * o.Scale);
        var h = (int)(SpikeRoot.BodyHeight * o.Scale);
        var crop = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            Buffer.BlockCopy(shot.Bgra, ((y0 + y) * shot.Stride) + (x0 * 4), crop, y * w * 4, w * 4);
        }

        var bmp = new WriteableBitmap(w, h);
        using (var s = bmp.PixelBuffer.AsStream())
        {
            await s.WriteAsync(crop);
        }

        bmp.Invalidate();
        _root.Standin.Child = new Image { Source = bmp, Stretch = Stretch.Fill };

        var menu = BuildMenu();
        var at = new Point(200, 150);
        menu.ShowAt(_root.Body, new FlyoutShowOptions { Position = at });
        await Task.Delay(500);
        var (rect, _) = OpenPopupRect();
        menu.Hide();
        await Task.Delay(300);
        if (rect is not { } r)
        {
            Check("Linux stand-in: MenuFlyout opens", false, "no open popup");
            return;
        }

        await OverlayPhaseAsync("airspace (Linux strategy): capture stand-in, WebView hidden, MenuFlyout", r, async () =>
        {
            _root.Standin.Visibility = Visibility.Visible;
            _root.Body.Visibility = Visibility.Collapsed;
            menu.ShowAt(_root.Body.Parent as FrameworkElement ?? _root, new FlyoutShowOptions { Position = new Point(at.X, at.Y) });
            await Task.Delay(700);
            menu.Hide();
            _root.Body.Visibility = Visibility.Visible;
            _root.Standin.Visibility = Visibility.Collapsed;
            await Task.Delay(300);
        });
    }
}
#endif
