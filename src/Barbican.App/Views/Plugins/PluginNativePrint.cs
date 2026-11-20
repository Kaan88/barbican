#if WINDOWS
// Native XAML printing of bounded, rasterized add-on PDF pages. The same page tree is used for preview and output.
using Barbican.App.Shell;
using Barbican.Core.Plugins.Bridge;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
#if !AUTOMATION
using Microsoft.UI.Xaml.Printing;
using Windows.Foundation;
using Windows.Graphics.Printing;
#endif
using Windows.Storage.Streams;

namespace Barbican.App.Views.Plugins;

internal static class PluginNativePrint
{
    private const int MaxBytes = 25 * 1024 * 1024;
    private const uint MaxPages = 20;
    private const double MaxDimension = 1440;
#if AUTOMATION
    internal static Action<IReadOnlyList<FrameworkElement>>? SyntheticIssue { get; set; }
    internal static Func<Func<bool>, CancellationToken, Task>? SyntheticAfterIssue { get; set; }
#endif

    /// <summary>Prepares all printable pages and issues the dialog only inside the caller's authority fence.</summary>
    internal static async Task<bool> PrintAsync(PluginPrintRequest request, string title, string? watermark,
        Func<Func<bool>, CancellationToken, Task<bool>> issue, Func<Func<bool>, bool> continueWhileCurrent, CancellationToken bindingLifetime, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(continueWhileCurrent);
        if (ActiveWindow.Current is not { } window || (request.Pdf is null) == (request.Html is null)) return false;
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        preparation.CancelAfter(TimeSpan.FromSeconds(30));
        List<FrameworkElement>? pages;
        try
        {
            var bytes = request.Pdf?.BinaryBytes() ?? await PluginHtmlPrintPreparation.PrepareAsync(request.Html!, preparation.Token).ConfigureAwait(true);
            if (bytes is not { Length: > 0 and <= MaxBytes }) return false;
            pages = await RenderAsync(bytes, watermark, preparation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        if (pages is null || pages.Count == 0 || ct.IsCancellationRequested) return false;
        return await IssuePreparedAsync(window, title, pages, issue, continueWhileCurrent, bindingLifetime, ct).ConfigureAwait(true);
    }

    private static async Task<bool> IssuePreparedAsync(Window window, string title, List<FrameworkElement> pages,
        Func<Func<bool>, CancellationToken, Task<bool>> issue, Func<Func<bool>, bool> continueWhileCurrent,
        CancellationToken bindingLifetime, CancellationToken ct)
    {
        var lifetimeGate = new Lock();
        var live = true;
        bool Continue(Func<bool> action)
        {
            if (bindingLifetime.IsCancellationRequested) return false;
            return continueWhileCurrent(() =>
            {
                lock (lifetimeGate) return live && !bindingLifetime.IsCancellationRequested && action();
            });
        }
        void Stop() { lock (lifetimeGate) live = false; }
#if AUTOMATION
        var synthetic = Barbican.Core.Storage.ProfilePaths.IsAutomationProfile(Environment.GetEnvironmentVariable("BARBICAN_PROFILE")) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PRINT"), "1", StringComparison.Ordinal);
        var sink = SyntheticIssue;
        if (!synthetic || sink is null) return false;
        var admitted = await issue(static () => true, ct).ConfigureAwait(true);
        if (!admitted) { Stop(); return false; }
        bool Publish() => Continue(() => { sink(pages); return true; });
        if (SyntheticAfterIssue is { } after) _ = ObserveSyntheticAsync(after, Publish, Stop, bindingLifetime);
        else { _ = Publish(); Stop(); }
        return true;
#else
        var document = CreateDocument(pages, Continue);
        return await IssueNativeAsync(window, title, document, issue, Continue, Stop, bindingLifetime, ct).ConfigureAwait(true);
#endif
    }

#if !AUTOMATION
    private static async Task<bool> IssueNativeAsync(Window window, string title, PrintDocument document,
        Func<Func<bool>, CancellationToken, Task<bool>> issue, Func<Func<bool>, bool> Continue,
        Action Stop, CancellationToken bindingLifetime, CancellationToken ct)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var lease = Barbican.App.Views.Calendar.NativePrintLease.TryAcquire(hwnd);
        if (lease is null) return false;
        PrintManager manager;
        try { manager = PrintManagerInterop.GetForWindow(hwnd); }
        catch { lease.Dispose(); throw; }
        var interaction = new PrintInteraction(manager, lease, document, title, Continue, Stop);
        IAsyncOperation<bool>? operation = null;
        var transferred = false;
        try
        {
            var admitted = await issue(() =>
            {
                if (ct.IsCancellationRequested || bindingLifetime.IsCancellationRequested || operation is not null) return false;
                operation = interaction.Show(hwnd);
                return true;
            }, ct).ConfigureAwait(true);
            if (!admitted || operation is null || !await operation.AsTask(ct).ConfigureAwait(true)) return false;
            transferred = true;
            _ = interaction.ObserveAsync(bindingLifetime);
            return true;
        }
        finally
        {
            if (!transferred) interaction.Dispose();
        }
    }

    private sealed class PrintInteraction(PrintManager manager, Barbican.App.Views.Calendar.NativePrintLease lease,
        PrintDocument document, string title, Func<Func<bool>, bool> useWhileCurrent, Action stop) : IDisposable
    {
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _claimed;
        private int _disposed;
        private bool _attached;
        private PrintTask? _printTask;

        public IAsyncOperation<bool> Show(nint hwnd)
        {
            manager.PrintTaskRequested += OnRequested;
            _attached = true;
            return PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd);
        }

        public async Task ObserveAsync(CancellationToken lifetime)
        {
            try
            {
#pragma warning disable VSTHRD003 // Both tasks are native event signals owned by this interaction and awaited asynchronously.
                if (await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime).ConfigureAwait(true))
                    _ = await _completed.Task.WaitAsync(lifetime).ConfigureAwait(true);
#pragma warning restore VSTHRD003
            }
#pragma warning disable CA1031 // This detached observer always releases the native window lease.
            catch (Exception) { }
#pragma warning restore CA1031
            finally { Dispose(); }
        }

        private void OnCompleted(PrintTask sender, PrintTaskCompletedEventArgs args) =>
            _completed.TrySetResult(args.Completion == PrintTaskCompletion.Submitted);

        private void OnRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
        {
            if (Interlocked.Exchange(ref _claimed, 1) != 0) return;
            var accepted = useWhileCurrent(() =>
            {
                _printTask = args.Request.CreatePrintTask(title, source => _ = useWhileCurrent(() =>
                {
                    source.SetSource(document.DocumentSource);
                    return true;
                }));
                _printTask.Completed += OnCompleted;
                return true;
            });
            _ready.TrySetResult(accepted);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            stop();
            if (_attached) Detach(() => manager.PrintTaskRequested -= OnRequested);
            if (_printTask is { } task) Detach(() => task.Completed -= OnCompleted);
            lease.Dispose();
        }
    }

    private static void Detach(Action unsubscribe)
    {
        try { unsubscribe(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }
#endif

#if !AUTOMATION
    private static PrintDocument CreateDocument(List<FrameworkElement> pages, Func<Func<bool>, bool> Continue)
    {
        var document = new PrintDocument();
        document.Paginate += (_, _) =>
        {
            if (!Continue(() => { document.SetPreviewPageCount(pages.Count, PreviewPageCountType.Final); return true; }))
                document.SetPreviewPageCount(0, PreviewPageCountType.Final);
        };
        document.GetPreviewPage += (_, e) => _ = Continue(() => { document.SetPreviewPage(e.PageNumber, pages[Math.Clamp(e.PageNumber - 1, 0, pages.Count - 1)]); return true; });
        document.AddPages += (_, _) =>
        {
            if (!Continue(() =>
            {
                foreach (var page in pages) document.AddPage(page);
                document.AddPagesComplete();
                return true;
            })) document.AddPagesComplete();
        };
        return document;
    }
#endif

#if AUTOMATION
    private static async Task ObserveSyntheticAsync(Func<Func<bool>, CancellationToken, Task> after, Func<bool> continuation,
        Action cleanup, CancellationToken lifetime)
    {
        try { await after(continuation, lifetime).ConfigureAwait(true); }
#pragma warning disable CA1031 // A detached synthetic observer cannot surface failures to the caller.
        catch (Exception) { }
#pragma warning restore CA1031
        finally { cleanup(); }
    }
#endif

    private static async Task<List<FrameworkElement>?> RenderAsync(byte[] bytes, string? watermark, CancellationToken ct)
    {
        try
        {
            using var source = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(source))
            {
                writer.WriteBytes(bytes);
                _ = await writer.StoreAsync().AsTask(ct).ConfigureAwait(true);
                _ = writer.DetachStream();
            }
            source.Seek(0);
            var document = await PdfDocument.LoadFromStreamAsync(source).AsTask(ct).ConfigureAwait(true);
            if (document.PageCount is 0 or > MaxPages) return null;
            var pages = new List<FrameworkElement>();
            for (uint index = 0; index < document.PageCount; index++)
            {
                using var page = document.GetPage(index);
                var size = page.Size;
                if (size.Width <= 0 || size.Height <= 0 || !double.IsFinite(size.Width) || !double.IsFinite(size.Height)) return null;
                var scale = Math.Min(1, MaxDimension / Math.Max(size.Width, size.Height));
                var options = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, Math.Ceiling(size.Width * scale)), DestinationHeight = (uint)Math.Max(1, Math.Ceiling(size.Height * scale)) };
                using var output = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(output, options).AsTask(ct).ConfigureAwait(true);
                output.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(output).AsTask(ct).ConfigureAwait(true);
                var grid = new Grid { Width = 816, Height = 1056, Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
                grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
                if (!string.IsNullOrWhiteSpace(watermark))
                {
                    var mark = new TextBlock { Text = watermark, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                        FontSize = 28, TextWrapping = TextWrapping.Wrap, MaxWidth = 760 };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(mark, "plugin.print.watermark");
                    grid.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.White), Padding = new Thickness(12),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = mark });
                }
                pages.Add(grid);
            }
            return pages;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException) { return null; }
    }
}
#endif
