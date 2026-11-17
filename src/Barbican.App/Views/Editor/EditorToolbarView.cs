// Mirrors: Bulwark components/email/rich-text-editor.tsx (the formatting toolbar: marks, size, colour, block, align, lists, link, image, table, clear, undo/redo, source, RTL; folds what does not fit into an
// overflow menu :875; roving arrow keys and Alt+F10 :977), Rust compose toolbar; spec/menus.md. The same items are the floating bubble (marks and link) over a selection.

using Barbican.Core.Editor;
using Barbican.Presentation.Editor;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Editor;

/// <summary>The formatting toolbar: fixed above the text, or the compact bubble near a selection.</summary>
public sealed class EditorToolbarView : UserControl
{
    private const double OverflowWidth = 36;

    private readonly bool _bubble;
    private readonly List<ToolbarItem> _items = [];
    private readonly StackPanel _main = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _more;
    private readonly List<Button> _overflowButtons = [];
    private ToolbarState _state = new();
    private bool _rtl;
    private int _fitting = int.MaxValue;

    public EditorToolbarView(bool bubble)
    {
        _bubble = bubble;
        _more = EditorUi.TextButton("overflow", "…", "More", () => PickerRequested?.Invoke("overflow", _more));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(_main);
        row.Children.Add(_more);
        Content = row;
        AutomationProperties.SetAutomationId(this, bubble ? "editor.bubble" : "editor.toolbar");
        AutomationProperties.SetName(this, bubble ? "Selection formatting" : "Formatting");
        Build();
        Rebuild();
        KeyDown += OnKeyDown;
    }

    /// <summary>A command from a button (the view runs it).</summary>
    public event Action<EditorCommand, string?>? CommandInvoked;

    /// <summary>A dropdown wants a card: "size", "color", "highlight", "block", "align", "table", "image", "overflow".</summary>
    public event Action<string, FrameworkElement>? PickerRequested;

    /// <summary>Escape in the toolbar hands the focus back to the text.</summary>
    public event Action? ReturnFocus;

    /// <summary>The buttons that do not fit (their own copies; the overflow card shows them).</summary>
    public IReadOnlyList<ToolbarItem> Overflowing => _fitting >= _items.Count ? [] : [.. _items.Skip(_fitting)];

    /// <summary>The items that are shown (hidden in plain-text mode by the host).</summary>
    public int ItemCount => _items.Count;

    public void Update(ToolbarState state, bool rtl)
    {
        _state = state;
        if (_rtl != rtl)
        {
            _rtl = rtl;
            Build();
            Rebuild();
        }

        foreach (var item in _items)
        {
            item.Refresh(item.Live, state);
        }

        foreach (var b in _overflowButtons)
        {
            if (_items.FirstOrDefault(i => string.Equals(AutomationProperties.GetAutomationId(b), "editor." + i.Id, StringComparison.Ordinal)) is { } owner)
            {
                owner.Refresh(b, state);
            }
        }
    }

    /// <summary>Fits the items to <paramref name="width"/>: those that do not go into the overflow card (the "..." button shows only then).</summary>
    public void Fit(double width)
    {
        if (_bubble)
        {
            return;
        }

        var fitting = 0;
        var used = 0.0;
        foreach (var item in _items)
        {
            item.Live.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 40));
            var w = item.Live.DesiredSize.Width + 2;
            if (used + w > width - OverflowWidth && fitting < _items.Count)
            {
                break;
            }

            used += w;
            fitting++;
        }

        if (fitting + 1 >= _items.Count && used <= width)
        {
            fitting = _items.Count;
        }

        if (fitting != _fitting)
        {
            _fitting = fitting;
            Rebuild();
        }
    }

    /// <summary>A fresh button of an item for the overflow card, kept in sync by <see cref="Update"/>.</summary>
    public Button CreateOverflowButton(ToolbarItem item)
    {
        var b = item.Create();
        if (item.Picker is { } kind)
        {
            b.Click += (_, _) => Picker(kind, b);
        }

        item.Refresh(b, _state);
        _overflowButtons.Add(b);
        return b;
    }

    /// <summary>Alt+F10: the first button takes the keyboard focus; arrows move along the toolbar.</summary>
    public void FocusFirst() => _ = _main.Children.OfType<Button>().FirstOrDefault()?.Focus(FocusState.Keyboard);

    private void Run(EditorCommand command, string? argument = null) => CommandInvoked?.Invoke(command, argument);

    private void Picker(string kind, Button anchor) => PickerRequested?.Invoke(kind, anchor);

    // MA0051: the item table in toolbar order; splitting it would hide that order.
#pragma warning disable MA0051
    private void Build()
    {
        _items.Clear();
        ToolbarItem Toggle(string id, string text, string tip, EditorCommand c, Func<ToolbarState, bool> on, bool bold = false, bool italic = false, bool underline = false, bool strike = false) =>
            new(id, () => EditorUi.TextButton(id, text, tip, () => Run(c), bold, italic, underline, strike), (b, s) => Mark(b, on(s)));
        ToolbarItem Drop(string id, string text, string tip, string kind, Func<ToolbarState, string>? label = null, Func<ToolbarState, bool>? on = null) =>
            new(id, () => EditorUi.TextButton(id, text, tip, () => { }), (b, s) =>
            {
                b.Content = new TextBlock { Text = (label?.Invoke(s) ?? text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                Mark(b, on?.Invoke(s) ?? false);
            })
            { Picker = kind };
        _items.Add(Toggle("bold", "B", "Bold (Ctrl+B)", EditorCommand.Bold, s => s.Bold, bold: true));
        _items.Add(Toggle("italic", "I", "Italic (Ctrl+I)", EditorCommand.Italic, s => s.Italic, italic: true));
        _items.Add(Toggle("underline", "U", "Underline (Ctrl+U)", EditorCommand.Underline, s => s.Underline, underline: true));
        _items.Add(Toggle("strike", "S", "Strikethrough (Ctrl+Shift+S)", EditorCommand.Strike, s => s.Strike, strike: true));
        _items.Add(Toggle("code", "</>", "Inline code (Ctrl+E)", EditorCommand.Code, s => s.Code));
        _items.Add(Toggle("highlight", "Hl", "Highlight (Ctrl+Shift+H)", EditorCommand.Highlight, s => s.Highlight));
        if (!_bubble)
        {
            _items.Add(Drop("size", "14", "Text size", "size", s => (s.Size ?? EditorCommandRunner.NormalSize).ToString(System.Globalization.CultureInfo.InvariantCulture) + " ▾", s => s.Size is not null));
            _items.Add(Drop("color", "A", "Text colour", "color", _ => "A ▾", s => s.Color is not null));
            _items.Add(Drop("block", "Normal", "Paragraph style", "block", s => BlockLabel(s.Block) + " ▾"));
            _items.Add(Toggle("bullet", "•", "Bulleted list (Ctrl+Shift+8)", EditorCommand.BulletList, s => s.Block is BlockKind.Bullet));
            _items.Add(Toggle("numbered", "1.", "Numbered list (Ctrl+Shift+7)", EditorCommand.NumberedList, s => s.Block is BlockKind.Ordered));
            _items.Add(Drop("align", "Left", "Alignment", "align", s => AlignLabel(s.Align) + " ▾"));
        }

        _items.Add(Toggle("link", "Link", "Link (Ctrl+K)", EditorCommand.Link, s => s.Link is not null));
        if (!_bubble)
        {
            _items.Add(Drop("image", "Image", "Insert image", "image"));
            _items.Add(Drop("table", "Table", "Insert table", "table"));
            _items.Add(Toggle("rule", "―", "Horizontal rule", EditorCommand.Rule, _ => false));
            if (_rtl)
            {
                _items.Add(Toggle("rtl", "RTL", "Right-to-left paragraph", EditorCommand.AlignRight, s => s.Align == Align.Right));
            }
        }

        _items.Add(Toggle("clear", "Tx", "Clear formatting (Ctrl+\\)", EditorCommand.ClearFormat, _ => false));
        if (!_bubble)
        {
            _items.Add(new ToolbarItem("undo", () => EditorUi.IconButton("undo", "Undo", "Undo (Ctrl+Z)", () => Run(EditorCommand.Undo)), (b, s) => b.IsEnabled = s.CanUndo));
            _items.Add(new ToolbarItem("redo", () => EditorUi.IconButton("redo", "Redo", "Redo (Ctrl+Y)", () => Run(EditorCommand.Redo)), (b, s) => b.IsEnabled = s.CanRedo));
            _items.Add(new ToolbarItem("source", () => EditorUi.TextButton("source", "HTML", "Edit HTML source", () => Run(EditorCommand.ToggleSource)), (_, _) => { }));
        }

        foreach (var item in _items)
        {
            item.Bind(this);
        }

        _fitting = int.MaxValue;
    }
#pragma warning restore MA0051

    private void Rebuild()
    {
        _main.Children.Clear();
        var shown = Math.Min(_fitting, _items.Count);
        for (var i = 0; i < shown; i++)
        {
            var item = _items[i];
            if (i > 0 && item.Id is "link" or "image" or "clear" or "undo" or "source" && !_bubble)
            {
                _main.Children.Add(EditorUi.Separator());
            }

            _main.Children.Add(item.Live);
        }

        _more.Visibility = shown < _items.Count ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _items)
        {
            item.Refresh(item.Live, _state);
        }
    }

    internal void InvokeItem(ToolbarItem item, Button source)
    {
        if (item.Picker is { } kind)
        {
            Picker(kind, source);
        }
    }

    private static void Mark(Button button, bool on)
    {
        button.Background = on ? Theme.AppResources.Brush("Chip") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetItemStatus(button, on ? "on" : "off");
    }

    private static string BlockLabel(BlockKind kind) => kind switch
    {
        BlockKind.Heading h => "Heading " + h.Level,
        BlockKind.Quote => "Quote",
        BlockKind.CodeBlock => "Code",
        BlockKind.Bullet => "Bulleted",
        BlockKind.Ordered => "Numbered",
        _ => "Normal",
    };

    private static string AlignLabel(Align align) => align switch
    {
        Align.Center => "Center",
        Align.Right => "Right",
        Align.Justify => "Justify",
        _ => "Left",
    };

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var buttons = _main.Children.OfType<Button>().ToList();
        var i = buttons.FindIndex(b => b.FocusState != FocusState.Unfocused);
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Right when i >= 0:
                _ = buttons[(i + 1) % buttons.Count].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left when i >= 0:
                _ = buttons[(i - 1 + buttons.Count) % buttons.Count].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Home when buttons.Count > 0:
                _ = buttons[0].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.End when buttons.Count > 0:
                _ = buttons[^1].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                ReturnFocus?.Invoke();
                e.Handled = true;
                break;
        }
    }
}
