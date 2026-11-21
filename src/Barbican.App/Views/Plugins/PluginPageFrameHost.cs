#if WINDOWS
// Mirrors: Bulwark components/plugins/plugin-page-app.tsx (the add-on's `app-page` slot fills the content area, props { pageId, path }; `fill` offers take the whole area, others are as tall as their content, plugin-iframe-slot.tsx)
// and runtime.tsx:1584 (the frame reports its content height). The frame is the add-on's own sandboxed runtime; this host only owns the panel it is drawn in and the size it gets.
using Barbican.App.Shell;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.App.Theme;
using Barbican.Core.Plugins.Protocol;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Views.Plugins;

/// <summary>Draws one add-on page frame in <see cref="Surface"/> and replaces or removes it when the page, path or screen changes.</summary>
internal sealed class PluginPageFrameHost
{
    private const double MaxFrameHeight = 2000; // Bulwark slot-layout.ts MAX_SLOT_HEIGHT
    private const string Slot = "app-page";
    private const string Permission = "ui:app-page";

    private readonly PluginSlotFrames _frames = ViewHost.Get<PluginSlotFrames>();
    private readonly Grid _mount = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly ScrollViewer _scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
    private readonly Border _surface = new();
    private readonly TextBlock _note = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _start;
    private IPluginSlotFrameInstance? _frame;
    private string? _shown;
    private bool _fill;

    public PluginPageFrameHost()
    {
        _mount.Children.Add(_note);
        SetLayout(false);
    }

    /// <summary>The element to place in the screen.</summary>
    public FrameworkElement Surface => _surface;

    public TextBlock Note => _note;

    /// <summary>The page frame is on screen (or starting).</summary>
    public bool Active => _shown is not null;

    /// <summary>Shows page <paramref name="pageId"/> of <paramref name="packageId"/> with <paramref name="path"/>; the same page and path keeps the running frame.</summary>
    public void Show(string packageId, string pageId, string path)
    {
        var id = packageId + "\n" + pageId + "\n" + path;
        if (string.Equals(id, _shown, StringComparison.Ordinal) && _frame?.Binding.IsAlive != false) return;
        Stop();
        _shown = id;
        if (_frames.Offer(packageId, Slot) is not { } fill)
        {
            SetNote("This add-on is not running, or does not draw this page.");
            return;
        }

        _fill = fill;
        SetLayout(fill);
        SetNote("Loading...");
        var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _start = budget;
        if (_surface.IsLoaded && _surface.XamlRoot is not null) _ = StartAsync(packageId, pageId, path, budget);
        else
        {
            void Ready(object sender, RoutedEventArgs e)
            {
                _surface.Loaded -= Ready;
                if (!budget.IsCancellationRequested) _ = StartAsync(packageId, pageId, path, budget);
            }

            _surface.Loaded += Ready;
        }
    }

    /// <summary>Removes the frame (the page was left): Bulwark unmounts the iframe, which ends its runtime.</summary>
    public void Stop()
    {
        _shown = null;
        var start = Interlocked.Exchange(ref _start, null);
        start?.Cancel();
        var frame = Interlocked.Exchange(ref _frame, null);
        if (frame is not null) _ = DisposeAsync(frame);
    }

    private static async Task DisposeAsync(IPluginSlotFrameInstance frame)
    {
        try { await frame.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // The frame is already unmounted; a cleanup failure has nothing to report to the page.
        catch (Exception) { }
#pragma warning restore CA1031
    }

    private async Task StartAsync(string packageId, string pageId, string path, CancellationTokenSource budget)
    {
        IPluginSlotFrameInstance? frame = null;
        try
        {
            var values = new PluginValueBuilder();
            var props = values.Build(values.Object(("pageId", values.String(pageId)), ("path", values.String(path))));
            var result = await _frames.StartAsync(packageId, Slot, Permission, props, _mount, PluginFrameTheme.Snapshot(_surface), budget.Token).ConfigureAwait(true);
            if (!result.IsOk || result.Value is not { } started)
            {
                if (!budget.IsCancellationRequested) SetNote("This add-on page could not be opened.");
                return;
            }

            frame = started;
            if (budget.IsCancellationRequested || !ReferenceEquals(_start, budget))
            {
                await frame.DisposeAsync().ConfigureAwait(true);
                return;
            }

            _frame = frame;
            if (!_fill)
            {
                frame.SlotResized += OnResized;
                var (height, _) = frame.LastSlotSize;
                if (height >= 0) Resize(height);
            }

            _note.Visibility = Visibility.Collapsed;
            _ = frame.Binding.Lifetime.Register(() => _queue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_frame, frame)) return;
                _frame = null;
                SetNote("The add-on stopped.");
            }));
            frame = null;
        }
        catch (OperationCanceledException) { }
#pragma warning disable CA1031 // A failing add-on must leave the screen usable.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (!budget.IsCancellationRequested) SetNote("This add-on page could not be opened.");
        }
        finally
        {
            if (frame is not null) await DisposeAsync(frame).ConfigureAwait(true);
        }
    }

    private void OnResized(object? sender, PluginSlotSizeEventArgs e) => _queue.TryEnqueue(() => Resize(e.Height));

    private void Resize(double height) => _mount.Height = Math.Clamp(Math.Ceiling(height), 0, MaxFrameHeight);

    private void SetLayout(bool fill)
    {
        // Only called while the mount holds nothing but the note, so it can change parents. A filling frame takes the whole area;
        // a content-sized one is as tall as the frame reports, and scrolls in the page when taller than the window.
        _surface.Child = null;
        _scroller.Content = null;
        _mount.Height = double.NaN;
        _mount.VerticalAlignment = fill ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        if (fill) _surface.Child = _mount;
        else
        {
            _scroller.Content = _mount;
            _surface.Child = _scroller;
        }
    }

    private void SetNote(string text)
    {
        _note.Text = text;
        _note.Visibility = Visibility.Visible;
    }

    /// <summary>The look of the area the frame sits in.</summary>
    public void ApplyLook(StyleState look)
    {
        var bento = look.IsBento && !look.IsBulwark;
        _surface.Background = look.SurfaceBrush;
        _surface.BorderBrush = bento || look.IsBulwark ? AppResources.Brush("Line") : null;
        _surface.BorderThickness = look.IsBulwark ? new Thickness(0) : look.PaneBorder;
        _surface.CornerRadius = look.PaneRadius;
        _surface.Margin = bento ? new Thickness(4) : new Thickness(0);
        _note.Foreground = AppResources.Brush("Faint");
    }
}
#endif
