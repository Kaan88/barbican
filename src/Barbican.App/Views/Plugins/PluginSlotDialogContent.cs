// Mirrors Bulwark components/plugins/plugin-dialog-host.tsx:405-470 (a custom dialog: the title row with the options menu and a close button, the host-written "From the plugin" line, then the add-on's own frame; the box is
// at most `width` (default 560) wide; a `fill` dialog has a fixed height as tall as the window lets it be up to 640, the others are as tall as their content with a scroller past 90% of the window) and plugin-iframe-slot.tsx
// (the frame reports its content height). Escape and the close button end the dialog without a result. Drawn inside the in-window dialog host; the frame is the add-on's own sandboxed runtime.
#if WINDOWS
using Barbican.App.Plugins.Sandbox;
using Barbican.App.Theme;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginSlotDialogContent : IDialogContent
{
    private const double DefaultWidth = 560, FillHeight = 640, MinFillHeight = 240, LoadingHeight = 120;
    private static readonly TimeSpan StartBudget = TimeSpan.FromSeconds(30);

    public bool OwnButtons => true;

    public double MaxWidth => DefaultWidth;

    public bool CanDraw(object request) => request is PluginSlotDialogHostRequest;

    public double MaxWidthOf(object request) => request is PluginSlotDialogHostRequest host ? host.Dialog.Width ?? DefaultWidth : DefaultWidth;

    public UIElement Create(ActiveDialog dialog) => new Form((PluginSlotDialogHostRequest)dialog.Request, dialog).Build();

    public DialogHeaderMenu? CreateHeaderMenu(ActiveDialog dialog)
    {
        var host = (PluginSlotDialogHostRequest)dialog.Request;
        return host.TurnOff is null ? null : new DialogHeaderMenu($"Options for {host.Source}", $"Turn off {host.Source}", () => TurnOffAsync(host, dialog));
    }

    private static async Task TurnOffAsync(PluginSlotDialogHostRequest host, ActiveDialog dialog)
    {
        try { await host.TurnOff!().ConfigureAwait(true); }
        finally { dialog.Complete(null); }
    }

#if AUTOMATION
    /// <summary>The form of the dialog on screen, for the synthetic frames probe (AUTOMATION builds only).</summary>
    internal static Form? Current { get; private set; }
#endif

    internal sealed class Form(PluginSlotDialogHostRequest host, ActiveDialog dialog) : IDisposable
    {
        private readonly Grid _mount = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        private readonly TextBlock _note = new() { Text = "Loading...", FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly CancellationTokenSource _lifetime = new();
        private IPluginSlotFrameInstance? _frame;
        private DispatcherQueue? _queue;
        private bool _stopped;
#if AUTOMATION
        private TextBlock? _source;
        private Button? _close;
        private StackPanel? _panel;
#endif

        internal StackPanel Build()
        {
            var request = host.Dialog;
            // what the card keeps for itself: its padding on both sides, its 1 px border and the 16 px gap of the icon column (collapsed, but its spacing stays): the content must fit in what is left of the dialog's width
            var padding = (DialogStyles.Bulwark ? 48 : 40) + 2 + 16 + 2;
            var panel = new StackPanel { Spacing = 12, MinWidth = Math.Max(280, (request.Width ?? DefaultWidth) - padding) };
            var source = new TextBlock { Text = $"From the add-on {host.Source}", FontSize = 12, Foreground = AppResources.Brush("Dim") };
            AutomationProperties.SetAutomationId(source, "plugin.slotdialog.source");
            var attribution = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0) };
            attribution.Children.Add(AddonGlyph.Create(12, AppResources.Brush("Dim")));
            attribution.Children.Add(source);
            panel.Children.Add(attribution);

            _note.Foreground = AppResources.Brush("Faint");
            _mount.Children.Add(_note);
            AutomationProperties.SetAutomationId(_note, "plugin.slotdialog.note");
            AutomationProperties.SetAutomationId(_mount, "plugin.slotdialog.frame");
            UIElement area;
            if (request.Fill)
            {
                _mount.Height = FillHeight;
                area = _mount;
            }
            else
            {
                _mount.Height = LoadingHeight;
                area = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, Content = _mount };
            }

            panel.Children.Add(area);
            var close = new Button { Content = "Close", Style = (Style)Application.Current.Resources["Button.Action"], HorizontalAlignment = HorizontalAlignment.Right };
            DialogStyles.Dress(close);
            AutomationProperties.SetAutomationId(close, "plugin.slotdialog.close");
            close.Click += (_, _) => dialog.Complete(null);
#if AUTOMATION
            _source = source;
            _close = close;
            _panel = panel;
            Current = this;
#endif
            panel.Children.Add(close);

            panel.Loaded += (_, _) =>
            {
                _queue = panel.DispatcherQueue;
                Fit(panel, area, request.Fill);
                _ = StartAsync(panel);
            };
            panel.Unloaded += (_, _) =>
            {
#if AUTOMATION
                if (ReferenceEquals(Current, this)) Current = null;
#endif
                Stop();
            };
            return panel;
        }

        // Bulwark: a filling dialog is as tall as the window lets it be (up to 640), the others scroll past 90% of the window.
        private void Fit(StackPanel panel, UIElement area, bool fill)
        {
            var window = panel.XamlRoot?.Size.Height ?? 800;
            if (fill) _mount.Height = Math.Clamp(window - 220, MinFillHeight, FillHeight);
            else if (area is ScrollViewer scroller) scroller.MaxHeight = Math.Max(MinFillHeight, (window * 0.9) - 190);
        }

        private async Task StartAsync(StackPanel panel)
        {
            IPluginSlotFrameInstance? started = null;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                budget.CancelAfter(StartBudget);
                var result = await host.StartFrame(_mount, PluginFrameTheme.Snapshot(panel), budget.Token).ConfigureAwait(true);
                if (!result.IsOk || result.Value is not { } frame)
                {
                    if (!_lifetime.IsCancellationRequested) _note.Text = "This add-on dialog could not be opened.";
                    return;
                }

                started = frame;
                if (_lifetime.IsCancellationRequested) return;
                _frame = frame;
                started = null;
                if (!host.Dialog.Fill)
                {
                    frame.SlotResized += OnResized;
                    var (height, _) = frame.LastSlotSize;
                    if (height >= 0) _mount.Height = Math.Max(1, Math.Ceiling(height));
                }

                _note.Visibility = Visibility.Collapsed;
                _ = frame.Binding.Lifetime.Register(() => _queue?.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(_frame, frame)) return;
                    _note.Text = "The add-on stopped.";
                    _note.Visibility = Visibility.Visible;
                }));
            }
            catch (OperationCanceledException) { }
#pragma warning disable CA1031 // A failing add-on must leave the dialog usable: the user can still close it.
            catch (Exception)
#pragma warning restore CA1031
            {
                if (!_lifetime.IsCancellationRequested) _note.Text = "This add-on dialog could not be opened.";
            }
            finally
            {
                if (started is not null) await DisposeAsync(started).ConfigureAwait(true);
            }
        }

        private void OnResized(object? sender, PluginSlotSizeEventArgs e) =>
            _ = _queue?.TryEnqueue(() => _mount.Height = Math.Clamp(Math.Ceiling(e.Height), 1, 2000));

        // The dialog ended (answered, closed, or the add-on went away): the frame is removed, which ends its runtime (Bulwark unmounts the iframe).
        private void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _lifetime.Cancel();
            var frame = Interlocked.Exchange(ref _frame, null);
            if (frame is not null) _ = DisposeAsync(frame);
        }

#if AUTOMATION
        /// <summary>What the dialog on screen shows, with the sizes the layout gave it; for the synthetic walkthrough.</summary>
        internal string Describe()
        {
            static string Box(FrameworkElement element, FrameworkElement relativeTo)
            {
                var at = element.TransformToVisual(relativeTo).TransformPoint(new Windows.Foundation.Point(0, 0));
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{at.X:0.#},{at.Y:0.#},{element.ActualWidth:0.#},{element.ActualHeight:0.#}");
            }

            var root = (FrameworkElement)_panel!.XamlRoot.Content;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                title = dialog.Title,
                source = _source?.Text,
                slot = host.Dialog.Slot,
                fill = host.Dialog.Fill,
                requestedWidth = host.Dialog.Width,
                content = Box(_panel, root),
                frame = Box(_mount, root),
                close = Box(_close!, root),
                window = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{root.ActualWidth:0.#}x{root.ActualHeight:0.#}"),
                note = _note.Visibility == Visibility.Visible ? _note.Text : null,
                mounted = _mount.Children.Select(child => child.GetType().Name).ToArray(),
                frameAlive = _frame?.Binding.IsAlive,
            });
        }

        /// <summary>Presses Close the way the automation peer's Invoke does.</summary>
        internal bool PressClose()
        {
            if (_close is null) return false;
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(_close)).Invoke();
            return true;
        }
#endif

        public void Dispose()
        {
            Stop();
            _lifetime.Dispose();
        }

        private static async Task DisposeAsync(IPluginSlotFrameInstance frame)
        {
            try { await frame.DisposeAsync().ConfigureAwait(false); }
#pragma warning disable CA1031 // The frame is already unmounted; a cleanup failure has nothing to report.
            catch (Exception) { }
#pragma warning restore CA1031
        }
    }
}
#endif
