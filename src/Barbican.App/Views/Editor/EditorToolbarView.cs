// Mirrors: Bulwark components/email/rich-text-editor.tsx:1030-1480 (the formatting toolbar, in its order and grouping: font size | bold, italic, underline, strikethrough, text colour, highlight | heading 1, heading 2 |
// bullet list, ordered list, quote, code block | align left, centre, right, text direction | link, picture, table | clear formatting | undo, redo; 16 px icons in 28 px buttons with 2 px gaps, a 1 x 20 separator between
// the groups :397-399, px-3 py-1.5; what does not fit goes into an overflow popover that wraps :1465-1490; roving arrow keys and Alt+F10 :977), Rust compose toolbar; spec/menus.md. The same items are the floating
// bubble (marks and link) over a selection. Barbican keeps three buttons Bulwark has no place for after Redo: inline code, horizontal rule and the HTML source (CP-06 in docs/milestones/bulwark-parity-gaps.md).

using Barbican.App.Controls;
using Barbican.Core.Editor;
using Barbican.Presentation.Editor;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Editor;

/// <summary>The formatting toolbar: fixed above the text, or the compact bubble near a selection.</summary>
public sealed class EditorToolbarView : UserControl
{
    private const double OverflowWidth = 30;
    private const double SeparatorWidth = 5;

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
        _more = EditorUi.IconButton("overflow", "More", "More formatting", () => PickerRequested?.Invoke("overflow", _more!));
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

#pragma warning disable MA0046 // internal Action events between the toolbar and its host
    /// <summary>A command from a button (the view runs it).</summary>
    public event Action<EditorCommand, string?>? CommandInvoked;

    /// <summary>A dropdown wants a card: "size", "color", "highlight", "block", "align", "table", "image", "overflow".</summary>
    public event Action<string, FrameworkElement>? PickerRequested;

    /// <summary>Escape in the toolbar hands the focus back to the text.</summary>
    public event Action? ReturnFocus;
#pragma warning restore MA0046

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

        double Width(ToolbarItem item)
        {
            item.Live.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 40));
            return item.Live.DesiredSize.Width + 2 + (SeparatorBefore(item) ? SeparatorWidth : 0);
        }

        var total = _items.Sum(Width);
        var fitting = 0;
        if (total <= width)
        {
            fitting = _items.Count;
        }
        else
        {
            var used = 0.0;
            foreach (var item in _items)
            {
                var w = Width(item);
                if (used + w > width - OverflowWidth)
                {
                    break;
                }

                used += w;
                fitting++;
            }
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
        ToolbarItem Icon(string id, string icon, string tip, EditorCommand c, Func<ToolbarState, bool>? on = null) =>
            new(id, () => EditorUi.IconButton(id, icon, tip, () => Run(c)), (b, s) => Mark(b, on?.Invoke(s) ?? false));
        ToolbarItem TextItem(string id, string text, string tip, EditorCommand c) =>
            new(id, () => EditorUi.TextButton(id, text, tip, () => Run(c)), (b, s) => Mark(b, false));
        ToolbarItem Pick(string id, string icon, string tip, string kind, Func<ToolbarState, bool>? on = null) =>
            new(id, () => EditorUi.IconButton(id, icon, tip, () => { }), (b, s) => Mark(b, on?.Invoke(s) ?? false)) { Picker = kind };
        // the first group: Bulwark's font size button shows two A's, or the chosen size (rich-text-editor.tsx:1068-1074)
        if (!_bubble)
        {
            _items.Add(new ToolbarItem("size", () => EditorUi.Make("size", SizeGlyph(null), "Font size", () => { }), (b, s) =>
            {
                b.Content = SizeGlyph(s.Size);
                Mark(b, s.Size is not null);
            })
            { Picker = "size" });
        }

        _items.Add(Icon("bold", "Bold", "Bold (Ctrl+B)", EditorCommand.Bold, s => s.Bold));
        _items.Add(Icon("italic", "Italic", "Italic (Ctrl+I)", EditorCommand.Italic, s => s.Italic));
        _items.Add(Icon("underline", "Underline", "Underline (Ctrl+U)", EditorCommand.Underline, s => s.Underline));
        _items.Add(Icon("strike", "Strike", "Strikethrough (Ctrl+Shift+S)", EditorCommand.Strike, s => s.Strike));
        if (_bubble)
        {
            _items.Add(Icon("code", "Code", "Inline code (Ctrl+E)", EditorCommand.Code, s => s.Code));
        }
        else
        {
            // the text colour icon takes the chosen colour (rich-text-editor.tsx:1165)
            _items.Add(new ToolbarItem("color", () => EditorUi.IconButton("color", "TextColor", "Text colour", () => { }), (b, s) =>
            {
                Mark(b, false);
                if (b.Content is SIcon icon)
                {
                    if (s.Color is { } hex && TryParseHex(hex, out var color))
                    {
                        icon.Foreground = new SolidColorBrush(color);
                    }
                    else
                    {
                        icon.ClearValue(Control.ForegroundProperty);
                    }
                }
            })
            { Picker = "color" });
        }

        _items.Add(Icon("highlight", "Highlight", "Highlight (Ctrl+Shift+H)", EditorCommand.Highlight, s => s.Highlight));
        if (!_bubble)
        {
            _items.Add(Icon("heading1", "Heading1", "Heading 1", EditorCommand.Heading1, s => s.Block is BlockKind.Heading { Level: 1 }));
            _items.Add(Icon("heading2", "Heading2", "Heading 2", EditorCommand.Heading2, s => s.Block is BlockKind.Heading { Level: 2 }));
            _items.Add(Icon("bullet", "List", "Bullet list (Ctrl+Shift+8)", EditorCommand.BulletList, s => s.Block is BlockKind.Bullet));
            _items.Add(Icon("numbered", "ListOrdered", "Ordered list (Ctrl+Shift+7)", EditorCommand.NumberedList, s => s.Block is BlockKind.Ordered));
            _items.Add(Icon("quote", "Quote", "Quote", EditorCommand.Quote, s => s.Block is BlockKind.Quote));
            _items.Add(Icon("codeblock", "Code", "Code block", EditorCommand.CodeBlock, s => s.Block is BlockKind.CodeBlock));
            _items.Add(Icon("alignleft", "AlignLeft", "Align left", EditorCommand.AlignLeft, s => s.Align == Align.Left));
            _items.Add(Icon("aligncenter", "AlignCenter", "Align center", EditorCommand.AlignCenter, s => s.Align == Align.Center));
            _items.Add(Icon("alignright", "AlignRight", "Align right", EditorCommand.AlignRight, s => s.Align == Align.Right));
            if (_rtl)
            {
                _items.Add(Icon("rtl", "TextDir", "Text direction (RTL/LTR)", EditorCommand.AlignRight, s => s.Align == Align.Right));
            }
        }

        _items.Add(Icon("link", "Link", "Link (Ctrl+K)", EditorCommand.Link, s => s.Link is not null));
        if (!_bubble)
        {
            _items.Add(Pick("image", "Image", "Picture", "image"));
            _items.Add(Pick("table", "Table", "Table", "table", s => s.InTable));
        }

        _items.Add(Icon("clear", "ClearFormat", "Clear formatting (Ctrl+\\)", EditorCommand.ClearFormat));
        if (!_bubble)
        {
            _items.Add(new ToolbarItem("undo", () => EditorUi.IconButton("undo", "Undo", "Undo (Ctrl+Z)", () => Run(EditorCommand.Undo)), (b, s) => b.IsEnabled = s.CanUndo));
            _items.Add(new ToolbarItem("redo", () => EditorUi.IconButton("redo", "Redo", "Redo (Ctrl+Y)", () => Run(EditorCommand.Redo)), (b, s) => b.IsEnabled = s.CanRedo));
            _items.Add(Icon("code", "Code", "Inline code (Ctrl+E)", EditorCommand.Code, s => s.Code));
            _items.Add(TextItem("rule", "―", "Horizontal rule", EditorCommand.Rule));
            _items.Add(new ToolbarItem("source", () => EditorUi.TextButton("source", "HTML", "Edit HTML source", () => Run(EditorCommand.ToggleSource)), (_, _) => { }));
        }

        foreach (var item in _items)
        {
            item.Bind(this);
        }

        _fitting = int.MaxValue;
    }
#pragma warning restore MA0051

    // Bulwark's font size glyph: a large and a small A on one baseline, or the number once a size is chosen (rich-text-editor.tsx:1068-1074)
    private static UIElement SizeGlyph(int? size)
    {
        if (size is { } px)
        {
            return new TextBlock { Text = px.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MinWidth = 24, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        var glyph = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 16 };
        glyph.Children.Add(new TextBlock { Text = "A", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
        glyph.Children.Add(new TextBlock { Text = "A", FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        return glyph;
    }

    private static bool TryParseHex(string hex, out Windows.UI.Color color)
    {
        color = default;
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    // a separator opens each group of Bulwark's toolbar (rich-text-editor.tsx:1118, 1241, 1261, 1299, 1344, 1437, 1447) and the Barbican extras
    private static readonly HashSet<string> GroupStarts = new(StringComparer.Ordinal) { "bold", "heading1", "bullet", "alignleft", "link", "clear", "undo", "code" };

    private bool SeparatorBefore(ToolbarItem item) => !_bubble && GroupStarts.Contains(item.Id) && _items.IndexOf(item) > 0;

    private void Rebuild()
    {
        _main.Children.Clear();
        var shown = Math.Min(_fitting, _items.Count);
        for (var i = 0; i < shown; i++)
        {
            var item = _items[i];
            if (i > 0 && SeparatorBefore(item))
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

    private static void Mark(Button button, bool on) => EditorUi.SetActive(button, on);

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
