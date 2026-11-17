using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Barbican;

/// Drives the prototype through the states of docs/milestones/M1-look-gap.md and renders each with RenderTargetBitmap
/// (XAML only; the WebView2 body is not in the picture, as in the M1 app's `shot`). Also writes the layout boxes of the
/// named regions to layout-<style>-<theme>.json so the numbers can be compared, not eyeballed.
public sealed partial class MainWindow
{
    public async Task RunCaptureAsync(string outDir)
    {
        var log = new StringBuilder();
        try
        {
            await Task.Delay(1500);
            foreach (var nordic in new[] { false, true })
            {
                foreach (var dark in new[] { true, false })
                {
                    Palette.Dark = dark;
                    ApplyTheme();
                    Palette.Nordic = nordic;
                    ApplyStyle(false);
                    var tag = (nordic ? "nordic" : "bento") + "-" + (dark ? "dark" : "light");
                    await ResizeAsync(1560, 960);

                    // inbox: the folder as it opens (first row selected, reader open on it)
                    FolderList.SelectedIndex = 0;
                    ShowFolder("Inbox");
                    MailList.SelectedIndex = 0;
                    await Task.Delay(700);
                    await ShotAsync(Path.Combine(outDir, $"proto-{tag}-inbox.png"), log);
                    DumpLayout(Path.Combine(outDir, $"layout-proto-{tag}.json"));

                    // selected: another mail open
                    MailList.SelectedIndex = 2;
                    await Task.Delay(500);
                    await ShotAsync(Path.Combine(outDir, $"proto-{tag}-selected.png"), log);

                    // hover: row 1 in PointerOver (no real pointer: the visual state is set in process)
                    if (MailList.ContainerFromIndex(1) is ListViewItem hover)
                    {
                        VisualStateManager.GoToState(hover, "PointerOver", false);
                        await Task.Delay(200);
                        await ShotAsync(Path.Combine(outDir, $"proto-{tag}-hover.png"), log);
                        VisualStateManager.GoToState(hover, "Normal", false);
                    }

                    // switch: select row 3 and render as fast as RenderTargetBitmap allows
                    var sw = Stopwatch.StartNew();
                    MailList.SelectedIndex = 3;
                    for (var i = 0; i < 4; i++)
                    {
                        await ShotAsync(Path.Combine(outDir, $"proto-{tag}-switch-{i}-{sw.ElapsedMilliseconds}ms.png"), log);
                    }

                    // resize: a narrow window
                    await ResizeAsync(1100, 760);
                    await Task.Delay(500);
                    await ShotAsync(Path.Combine(outDir, $"proto-{tag}-narrow.png"), log);
                    await ResizeAsync(1560, 960);
                }
            }
        }
        catch (Exception e)
        {
            log.AppendLine("ERROR " + e);
        }

        File.WriteAllText(Path.Combine(outDir, "proto-capture.log"), log.ToString());
        Application.Current.Exit();
    }

    async Task ResizeAsync(int w, int h)
    {
        var s = Hidden.Scale(this);
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(w * s), (int)(h * s)));
        await Task.Delay(400);
    }

    async Task ShotAsync(string path, StringBuilder log)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(Root);
        var px = (await rtb.GetPixelsAsync()).ToArray();
        using var stream = new InMemoryRandomAccessStream();
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, px);
        await enc.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using (var r = new DataReader(stream.GetInputStreamAt(0)))
        {
            await r.LoadAsync((uint)stream.Size);
            r.ReadBytes(bytes);
        }

        File.WriteAllBytes(path, bytes);
        log.AppendLine(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)} {rtb.PixelWidth}x{rtb.PixelHeight}");
    }

    void DumpLayout(string path)
    {
        var boxes = new List<string>();
        void Box(string name, FrameworkElement e)
        {
            if (e == null || e.ActualWidth <= 0) return;
            var p = e.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
            boxes.Add(string.Create(CultureInfo.InvariantCulture, $"\"{name}\": [{p.X:0.#}, {p.Y:0.#}, {e.ActualWidth:0.#}, {e.ActualHeight:0.#}]"));
        }

        Box("rail", RailTile);
        Box("sidebar", FolderTile);
        Box("compose", ComposeBtn);
        Box("list", ListTile);
        Box("list.title", FolderTitle);
        Box("reader", ReaderTile);
        Box("context", ContextTile);
        Box("reader.subject", Palette.Nordic ? NordSubject : SubjectText);
        Box("reader.body", BodyHost);
        for (var i = 0; i < 4; i++) Box($"row{i}", MailList.ContainerFromIndex(i) as FrameworkElement);
        for (var i = 0; i < 3; i++) Box($"folder{i}", FolderList.ContainerFromIndex(i) as FrameworkElement);
        for (var i = 0; i < Math.Min(3, railButtons.Count); i++) Box($"railbtn{i}", railButtons[i]);
        File.WriteAllText(path, "{\n  " + string.Join(",\n  ", boxes) + "\n}\n");
    }
}
