// Mirrors: Rust src/rte/mod.rs (refresh :244, the painted document, caret and toolbar state pushed to the UI) and ui composer editor (RteEditor surface); ADR 0006 sections 4 to 6 (canvas host, XAML caret and
// IME overlay, selection painted in Skia); Bulwark components/email/rich-text-editor.tsx. M3 plan 6.3 step 2 and 3. Owner editor-view.

using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Editor;
using Barbican.Editor.Layout;
using Barbican.Editor.Layout.Input;
using Barbican.Presentation.Editor;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;

namespace Barbican.App.Views.Editor;

/// <summary>
/// The rich-text editor control: the document drawn by <see cref="DocLayout"/> on a Skia canvas, a XAML caret and the hidden text box that carries typed and composed text over it, the toolbar and
/// bubble, popover cards, the source view. The composer puts it in its layout and gives it the <see cref="IEditorViewModel"/>; it reads and writes only the view model's session.
/// </summary>
public sealed partial class EditorView : UserControl
{
    private const double PadX = 4;
    private const double PadY = 6;

    private readonly EditorCanvas _canvas = new() { IsHitTestVisible = false };
    private readonly ThemeController _theme = ViewHost.Get<ThemeController>();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, Background = null };
    private readonly Grid _spacer = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), AllowDrop = true };
    private readonly Grid _body = new();
    private readonly Canvas _overlay = new();
    private readonly Rectangle _caret = new() { Width = 1.5, IsHitTestVisible = false };
    private readonly TextBox _ime;
    private readonly TextBlock _placeholder;
    private readonly TextBox _source;
    private readonly EditorToolbarView _toolbar = new(bubble: false);
    private readonly EditorToolbarView _bubble = new(bubble: true);
    private readonly Border _toolbarBar;
    private readonly Border _bubbleCard;
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private IEditorSurface? _vm;
    private IEditorSession? _session;
    private DocLayout? _layout;
    private EditorController? _ctl;
    private bool _blinkOn = true;
    private bool _composing;
    private bool _settingSource;
    private bool _rtl;

    public EditorView()
    {
        AppResources.EnsureStyles();
        _ime = new TextBox
        {
            Width = 4,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Opacity = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            IsSpellCheckEnabled = false,
            AcceptsReturn = false,
        };
        AutomationProperties.SetAutomationId(_ime, "editor.input");
        AutomationProperties.SetName(_ime, "Message body");
        _placeholder = new TextBlock { Text = "Write your message…", FontSize = 15, Foreground = AppResources.Brush("Faint"), IsHitTestVisible = false, Margin = new Thickness(PadX, PadY, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        _source = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Visibility = Visibility.Collapsed,
            Background = AppResources.Brush("Tile2"),
            Foreground = AppResources.Brush("Body"),
            IsSpellCheckEnabled = false,
        };
        AutomationProperties.SetAutomationId(_source, "editor.source");
        AutomationProperties.SetName(_source, "HTML source");
        _toolbarBar = new Border { Child = _toolbar, BorderBrush = AppResources.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 2, 4, 2) };
        _bubbleCard = new Border
        {
            Child = _bubble,
            Background = AppResources.Brush("Popup"),
            BorderBrush = AppResources.Brush("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Visibility = Visibility.Collapsed,
        };
        BuildTree();
        WireEvents();
        AutomationProperties.SetAutomationId(this, "editor");
        AutomationProperties.SetName(this, "Editor");
        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
    }

    public EditorView(IEditorViewModel viewModel)
        : this()
    {
        ViewModel = viewModel;
    }

    /// <summary>The editor's view model (the composer creates it from <see cref="IEditorViewModelFactory"/>); setting it attaches the control to its session.</summary>
    public IEditorViewModel? ViewModel
    {
        get => _vm;
        set => Attach(value);
    }

    /// <summary>The settings the view follows (RTL controls, plain text); the composer sets them from the user's settings.</summary>
    public bool RtlSupport
    {
        get => _rtl;
        set
        {
            _rtl = value;
            if (_vm is not null)
            {
                _vm.RtlSupport = value;
            }

            RefreshToolbar();
        }
    }

    /// <summary>The document point under the caret and the toolbar state, for probes.</summary>
    internal EditorController? Controller => _ctl;

    internal DocLayout? LayoutEngine => _layout;

    internal IEditorSurface? Surface => _vm;

    internal double ScrollOffset => _scroll.VerticalOffset;

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorAutomationPeer(this);

    /// <summary>Takes the keyboard focus (opening the composer, after a toolbar command).</summary>
    public void FocusEditor()
    {
        if (_vm?.SourceOpen == true)
        {
            _ = _source.Focus(FocusState.Programmatic);
            return;
        }

        _ = _ime.Focus(FocusState.Programmatic);
    }

    /// <summary>The text of the document, for the automation peer (Narrator reads it).</summary>
    internal string PlainText() => _session is null ? string.Empty : DocHtml.ToText(_session.Doc);

    private void BuildTree()
    {
        _spacer.Children.Add(new Border { Height = 20 });
        _scroll.Content = _spacer;
        _body.Children.Add(_canvas);
        _body.Children.Add(_placeholder);
        _body.Children.Add(_scroll);
        _overlay.Children.Add(_caret);
        _overlay.Children.Add(_ime);
        _overlay.Children.Add(_bubbleCard);
        _body.Children.Add(_overlay);
        _body.Children.Add(_source);
        BuildCardLayer();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(_toolbarBar);
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);
        Content = root;
    }

    private void Attach(IEditorViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _vm))
        {
            return;
        }

        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
        }

        if (_vm is not null)
        {
            _vm.ToolbarChanged -= OnToolbarChanged;
            _vm.FocusRequested -= OnFocusRequested;
            _vm.ViewCommandRequested -= OnViewCommand;
        }

        _vm = viewModel switch
        {
            null => null,
            IEditorSurface s => s,
            _ => new EditorViewModel(viewModel.Session, ViewHost.Find<Barbican.Core.Editor.Spell.ISpellEngine>(), ViewHost.Find<IRewriteProvider>()),
        };
        _session = _vm?.Session;
        if (_vm is null || _session is null)
        {
            _ctl = null;
            _layout = null;
            return;
        }

        _vm.RtlSupport = _rtl;
        _layout = new DocLayout(new RichTextKitEngine(), EditorPalette.Current());
        _ctl = new EditorController(_session, _layout);
        _canvas.Draw = Paint;
        _session.Changed += OnSessionChanged;
        _vm.ToolbarChanged += OnToolbarChanged;
        _vm.FocusRequested += OnFocusRequested;
        _vm.ViewCommandRequested += OnViewCommand;
        RefreshToolbar();
        Refresh(scrollToCaret: false);
    }

    private void OnLoaded()
    {
        _theme.CssApplied += OnThemeApplied;
        if (_vm is null)
        {
            return;
        }

        _ = _layout?.SetStyle(EditorPalette.Current());
        _blink.Tick += OnBlink;
        _blink.Start();
        Refresh(scrollToCaret: false);
    }

    private void OnUnloaded()
    {
        _theme.CssApplied -= OnThemeApplied;
        _blink.Stop();
        _blink.Tick -= OnBlink;
        if (_vm is not null)
        {
            ViewHost.Find<ActiveEditor>()?.Deactivate(_vm);
        }
    }

    private void OnThemeApplied(object? sender, ThemeCssEventArgs e)
    {
        if (_layout?.SetStyle(EditorPalette.Current()) == true) Refresh(scrollToCaret: false);
    }

    private void OnFocusRequested(object? sender, EventArgs e) => FocusEditor();

    private void OnBlink(object? sender, object e)
    {
        _blinkOn = !_blinkOn || MotionRuntime.Scale.Reduce;
        _caret.Opacity = _blinkOn && _session?.Sel.IsCollapsed == true ? 1 : 0;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Refresh(scrollToCaret: true);

    private void OnToolbarChanged(object? sender, EventArgs e)
    {
        RefreshToolbar();
        SyncSource();
    }

    /// <summary>Lays the document out at the current width, sizes the scroll area and repaints; the overlays follow.</summary>
    internal void Refresh(bool scrollToCaret)
    {
        if (_ctl is null || _layout is null || _session is null)
        {
            return;
        }

        var width = Math.Max((_scroll.ActualWidth > 0 ? _scroll.ActualWidth : ActualWidth) - (2 * PadX) - 12, 80);
        _ctl.Build((float)width);
        _spacer.Height = _layout.Height + (2 * PadY) + 24;
        _placeholder.Visibility = _session.Doc.IsEmpty && !_composing ? Visibility.Visible : Visibility.Collapsed;
        if (scrollToCaret)
        {
            ScrollToCaret();
        }

        _canvas.Redraw();
        UpdateOverlays();
    }

    private void Paint(SKCanvas canvas, float scale)
    {
        canvas.Clear(SKColors.Transparent);
        if (_layout is null || _session is null || _vm is null)
        {
            return;
        }

        var offset = (float)_scroll.VerticalOffset;
        canvas.Save();
        canvas.Scale(scale);
        canvas.Translate((float)PadX, (float)PadY - offset);
        var focus = _session.Sel.Focus.B;
        var onImage = _session.Doc.Blocks.Count > focus && _session.Doc.Blocks[focus].Kind is BlockKind.Image;
        var options = new PaintOptions
        {
            ClipTop = offset - (float)PadY,
            ClipBottom = offset + (float)_body.ActualHeight,
            SpellMarks = (b, text) => _vm.Misspelled(b, text),
            SelectedImage = onImage ? focus : -1,
        };
        _layout.Paint(canvas, _session.Doc, _session.Sel, options);
        canvas.Restore();
    }

    private void ScrollToCaret()
    {
        if (_ctl is null)
        {
            return;
        }

        var c = _ctl.Caret();
        var view = _body.ActualHeight;
        if (view <= 0)
        {
            return;
        }

        var top = c.Y + PadY;
        var bottom = top + c.Height;
        var offset = _scroll.VerticalOffset;
        if (top < offset)
        {
            _ = _scroll.ChangeView(null, Math.Max(0, top - 8), null, disableAnimation: true);
        }
        else if (bottom > offset + view)
        {
            _ = _scroll.ChangeView(null, bottom - view + 8, null, disableAnimation: true);
        }
    }

    private void RefreshToolbar()
    {
        if (_vm is null)
        {
            return;
        }

        var plain = _vm.Plain;
        _toolbarBar.Visibility = plain ? Visibility.Collapsed : Visibility.Visible;
        _toolbar.Update(_vm.Toolbar, _rtl);
        _bubble.Update(_vm.Toolbar, _rtl);
        UpdateOverlays();
    }

    private void SyncSource()
    {
        if (_vm is null)
        {
            return;
        }

        var open = _vm.SourceOpen;
        if (open && !string.Equals(_source.Text, _vm.SourceText, StringComparison.Ordinal))
        {
            _settingSource = true;
            _source.Text = _vm.SourceText;
            _settingSource = false;
        }

        _source.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        _overlay.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        if (open)
        {
            _ = _source.Focus(FocusState.Programmatic);
        }
        else
        {
            Refresh(scrollToCaret: true);
            FocusEditor();
        }
    }
}
