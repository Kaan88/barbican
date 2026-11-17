// Mirrors: Rust src/rte/mod.rs refresh :244 (caret, floating toolbar position), resize :399 (image corner handles); Bulwark rich-text-editor.tsx (bubble menu, link popover, @ list, image alt dialog);
// ADR 0006 "Overlays" (caret, IME box, toolbar, link popover and image handles are XAML elements over the canvas); ADR 0007 (every card is in-window, never a Popup outside it).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Editor;
using Barbican.Editor.Layout;
using Barbican.Presentation.Editor;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

namespace Barbican.App.Views.Editor;

public sealed partial class EditorView
{
    private readonly Grid _layer = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Visibility = Visibility.Collapsed };
    private readonly Canvas _cardCanvas = new();
    private readonly Rectangle[] _handles = new Rectangle[4];
    private FrameworkElement? _card;
    private FrameworkElement? _popover;
    private FrameworkElement? _mentionList;
    private string? _linkPopoverFor;
    private int _mentionStart = -1;
    private int _mentionSelected;
    private IReadOnlyList<MentionCandidate> _mentionItems = [];

    private void BuildCardLayer()
    {
        _layer.Children.Add(_cardCanvas);
        _layer.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _layer))
            {
                CloseCard();
                e.Handled = true;
            }
        };
        _body.Children.Add(_layer);
        string[] corners = ["nw", "ne", "sw", "se"];
        for (var i = 0; i < 4; i++)
        {
            var h = new Rectangle { Width = 10, Height = 10, Fill = AppResources.Brush("Accent"), Stroke = AppResources.Brush("Ground"), StrokeThickness = 1, Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(h, "editor.handle." + corners[i]);
            AutomationProperties.SetName(h, "Resize image " + corners[i]);
            var left = i % 2 == 0;
            h.PointerPressed += (_, e) => OnHandleDown(h, left, e);
            h.PointerMoved += OnHandleMoved;
            h.PointerReleased += OnHandleUp;
            _handles[i] = h;
            _overlay.Children.Add(h);
        }
    }

    private double ContentX(PointerRoutedEventArgs e) => e.GetCurrentPoint(_spacer).Position.X - PadX;

    private void OnHandleDown(Rectangle handle, bool left, PointerRoutedEventArgs e)
    {
        if (_ctl is not null && _ctl.ResizeBegin(left, (float)ContentX(e)))
        {
            _ = handle.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnHandleMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_ctl is { Resizing: true } c && c.ResizeMove((float)ContentX(e)))
        {
            e.Handled = true;
        }
    }

    private void OnHandleUp(object sender, PointerRoutedEventArgs e)
    {
        _ctl?.ResizeEnd();
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    /// <summary>Moves the caret, the IME box, the image handles, the bubble and the popovers to the current state.</summary>
    internal void UpdateOverlays()
    {
        if (_ctl is null || _session is null || _vm is null || _layout is null)
        {
            return;
        }

        var offset = _scroll.VerticalOffset;
        var c = _ctl.Caret();
        var (x, y) = (PadX + c.X, PadY + c.Y - offset);
        var focusBlock = Math.Clamp(_session.Sel.Focus.B, 0, _session.Doc.Blocks.Count - 1);
        var onImage = _session.Doc.Blocks[focusBlock].Kind is BlockKind.Image;
        Canvas.SetLeft(_caret, x);
        Canvas.SetTop(_caret, y);
        _caret.Height = Math.Max(c.Height, 8);
        _caret.Fill = AppResources.Brush("Accent");
        _caret.Opacity = _session.Sel.IsCollapsed && !onImage && _blinkOn ? 1 : 0;
        Canvas.SetLeft(_ime, x);
        Canvas.SetTop(_ime, y);
        PlaceHandles(focusBlock, onImage, offset);
        PlaceBubble(offset);
        PlacePopover(x, y, c.Height);
        PlaceMentions(x, y, c.Height);
    }

    private void PlaceHandles(int block, bool onImage, double offset)
    {
        if (_layout is null)
        {
            return;
        }

        var rect = onImage ? _layout.ImageRect(block) : null;
        for (var i = 0; i < _handles.Length; i++)
        {
            var h = _handles[i];
            h.Visibility = rect is null ? Visibility.Collapsed : Visibility.Visible;
            if (rect is { } r)
            {
                var hx = PadX + r.X + ((i % 2 == 0) ? 0 : r.Width) - 5;
                var hy = PadY + r.Y - offset + ((i < 2) ? 0 : r.Height) - 5;
                Canvas.SetLeft(h, hx);
                Canvas.SetTop(h, hy);
            }
        }
    }

    private void PlaceBubble(double offset)
    {
        var show = _vm is { Plain: false, SourceOpen: false } && _vm.Toolbar.HasSelection && !_pointerDown && _session is not null && !_session.Sel.IsCollapsed
            && _session.Doc.Blocks[Math.Clamp(_session.Sel.Focus.B, 0, _session.Doc.Blocks.Count - 1)].Kind is not BlockKind.Image;
        _bubbleCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || _layout is null || _session is null)
        {
            return;
        }

        var rects = _layout.SelectionRects(_session.Doc, _session.Sel);
        if (rects.Count == 0)
        {
            _bubbleCard.Visibility = Visibility.Collapsed;
            return;
        }

        _bubbleCard.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var first = rects[0];
        var x = Math.Clamp(PadX + first.X, 4, Math.Max(4, _body.ActualWidth - _bubbleCard.DesiredSize.Width - 4));
        var y = PadY + first.Y - offset - _bubbleCard.DesiredSize.Height - 6;
        if (y < 2)
        {
            var last = rects[^1];
            y = PadY + last.Y + last.Height - offset + 6;
        }

        Canvas.SetLeft(_bubbleCard, x);
        Canvas.SetTop(_bubbleCard, Math.Max(2, y));
    }

    private void PlacePopover(double x, double y, double height)
    {
        if (_popover is null)
        {
            return;
        }

        Canvas.SetLeft(_popover, Math.Clamp(x, 4, Math.Max(4, _body.ActualWidth - 300)));
        Canvas.SetTop(_popover, y + height + 6);
    }

    private void PlaceMentions(double x, double y, double height)
    {
        if (_mentionList is null)
        {
            return;
        }

        Canvas.SetLeft(_mentionList, Math.Clamp(x, 4, Math.Max(4, _body.ActualWidth - 300)));
        Canvas.SetTop(_mentionList, y + height + 4);
    }

    // ------------------------------------------------------------------ cards (dropdowns and dialogs)

    private void ShowCard(FrameworkElement card, Windows.Foundation.Point at)
    {
        CloseCard();
        _card = card;
        _cardCanvas.Children.Add(card);
        card.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(card, Math.Clamp(at.X, 4, Math.Max(4, _body.ActualWidth - card.DesiredSize.Width - 4)));
        Canvas.SetTop(card, Math.Clamp(at.Y, 2, Math.Max(2, _body.ActualHeight - card.DesiredSize.Height - 4)));
        _layer.Visibility = Visibility.Visible;
        AutomationProperties.SetAutomationId(card, "editor.card");
    }

    private void CenterCard(FrameworkElement card)
    {
        card.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        ShowCard(card, new Windows.Foundation.Point((_body.ActualWidth - card.DesiredSize.Width) / 2, Math.Max(8, (_body.ActualHeight - card.DesiredSize.Height) / 3)));
    }

    private Windows.Foundation.Point Below(FrameworkElement anchor)
    {
        var p = anchor.TransformToVisual(_body).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight + 2));
        return p;
    }

    /// <summary>Closes the open dropdown or dialog.</summary>
    internal void CloseCard()
    {
        if (_card is null)
        {
            return;
        }

        _cardCanvas.Children.Clear();
        _card = null;
        _layer.Visibility = Visibility.Collapsed;
        FocusEditor();
    }

    /// <summary>Escape closes the topmost transient thing first: a card, the @ list, the link popover (and only that); true when something closed.</summary>
    internal bool CloseTransient()
    {
        if (_card is not null)
        {
            CloseCard();
            return true;
        }

        if (_mentionList is not null)
        {
            CloseMentions();
            return true;
        }

        if (_popover is not null)
        {
            ClosePopover();
            return true;
        }

        return false;
    }

    private void OnPickerRequested(string kind, FrameworkElement anchor)
    {
        if (_vm is null)
        {
            return;
        }

        var at = Below(anchor);
        var t = _vm.Toolbar;
        FrameworkElement? card = kind switch
        {
            "size" => EditorCards.SizePicker(t.Size, px => Run(EditorCommand.Size, px?.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            "color" => EditorCards.ColorPicker(t.Color, hex => Run(EditorCommand.Color, hex)),
            "block" => EditorCards.BlockPicker(t.Block, c => Run(c)),
            "align" => EditorCards.AlignPicker(t.Align, c => Run(c)),
            "image" => EditorCards.ImagePicker(() => { CloseCard(); _ = PickImagesAsync(); }, () => { CloseCard(); OpenImageUrlDialog(); }),
            "table" => EditorCards.TableGrid((r, c) => Run(EditorCommand.Table, FormattableString.Invariant($"{r}x{c}"))),
            "overflow" => OverflowCard(),
            _ => null,
        };
        if (card is not null)
        {
            ShowCard(card, at);
        }
    }

    private FrameworkElement OverflowCard()
    {
        var stack = new StackPanel { Spacing = 2 };
        foreach (var item in _toolbar.Overflowing)
        {
            var b = _toolbar.CreateOverflowButton(item);
            if (item.Picker is null)
            {
                b.Click += (_, _) => CloseCard();
            }

            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(b);
        }

        return EditorUi.Card(stack);
    }

    private void Run(EditorCommand command, string? argument = null)
    {
        _ = _vm?.Execute(command, argument);
        CloseCard();
    }

    // ------------------------------------------------------------------ commands that need the view

    private void OnViewCommand(object? sender, EditorViewCommandEventArgs e)
    {
        switch (e.Command)
        {
            case EditorCommand.Link:
                OpenLinkDialog();
                break;
            case EditorCommand.LinkOpen:
                OpenLink();
                break;
            case EditorCommand.LinkCopy:
                CopyLinkAddress();
                break;
            case EditorCommand.LinkRemove:
                _session?.SetLink(string.Empty, string.Empty);
                ClosePopover();
                break;
            case EditorCommand.Copy:
                CopySelection(cut: false);
                break;
            case EditorCommand.Cut:
                CopySelection(cut: true);
                break;
            case EditorCommand.Paste:
                _ = PasteAsync(plainOnly: false);
                break;
            case EditorCommand.PastePlain:
                _ = PasteAsync(plainOnly: true);
                break;
            case EditorCommand.Image:
                _ = PickImagesAsync();
                break;
            case EditorCommand.ImageFromUrl:
                OpenImageUrlDialog();
                break;
            case EditorCommand.ImageSize:
                ResizeSelectedImage(int.TryParse(e.Argument, out var w) ? w : 0);
                break;
            default:
                break;
        }
    }

    private void CopySelection(bool cut)
    {
        if (_session is null)
        {
            return;
        }

        var sink = new Snapshot();
        if (EditorClipboardActions.Copy(_session, sink, cut) && sink.Html is not null)
        {
            EditorClipboards.Backend.Write(sink.Html, sink.Text ?? string.Empty);
        }
    }

    /// <summary>Pastes what the clipboard backend holds (the automation backend is in-process).</summary>
    internal async Task PasteAsync(bool plainOnly)
    {
        if (_session is null)
        {
            return;
        }

        var snapshot = await EditorClipboards.Backend.SnapshotAsync();
        EditorClipboardActions.Paste(_session, snapshot, plainOnly, ImageSize);
    }

    private async Task PickImagesAsync()
    {
        if (_session is null)
        {
            return;
        }

        foreach (var file in await EditorFilePicker.PickImagesAsync())
        {
            EditorClipboardActions.InsertImage(_session, file.Bytes, file.Mime, file.Name, ImageSize);
        }

        FocusEditor();
    }

    private void ResizeSelectedImage(int width)
    {
        if (_session is null || _layout is null)
        {
            return;
        }

        var b = _session.Sel.Focus.B;
        if (_layout.ImageRect(b) is not { } r)
        {
            return;
        }

        var height = width == 0 ? 0 : (int)Math.Round(width * r.Height / Math.Max(r.Width, 1f));
        _ = _session.ResizeImage(b, width, height, undo: true);
    }

    private void OpenLink()
    {
        if (_session?.CurrentLink() is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            _ = ViewHost.Find<Barbican.Platform.IBrowserLauncher>()?.OpenAsync(uri, CancellationToken.None);
        }
    }

    private void CopyLinkAddress()
    {
        if (_session?.CurrentLink() is { } url)
        {
            EditorClipboards.Backend.Write(System.Net.WebUtility.HtmlEncode(url), url);
        }
    }

    // ------------------------------------------------------------------ dialogs

    private void OpenLinkDialog()
    {
        if (_session is null)
        {
            return;
        }

        var url = _session.CurrentLink() ?? string.Empty;
        var text = _session.Sel.IsCollapsed
            ? (_session.LinkRange() is { } range ? _session.Doc.Blocks[range.Block].Text[range.Start..range.End] : string.Empty)
            : _session.SelectedText();
        ClosePopover();
        var editing = url.Length > 0;
        var card = EditorCards.LinkDialog(
            text,
            url,
            (t, href) =>
            {
                _session.SetLink(href, t);
                CloseCard();
            },
            editing ? () =>
            {
                _session.SetLink(string.Empty, string.Empty);
                CloseCard();
            }
            : null,
            CloseCard);
        CenterCard(card);
        FocusFirstField(card);
    }

    private void OpenImageUrlDialog()
    {
        var card = EditorCards.ImageUrlDialog(
            (src, alt) =>
            {
                _session?.InsertImage(new ImageData { Src = src, Alt = alt, Name = alt });
                CloseCard();
            },
            CloseCard);
        CenterCard(card);
        FocusFirstField(card);
    }

    private void OpenAltDialog()
    {
        if (_session is null || _session.Doc.Blocks[_session.Sel.Focus.B].Kind is not BlockKind.Image img)
        {
            return;
        }

        var block = _session.Sel.Focus.B;
        var card = EditorCards.AltText(
            img.Data.Alt,
            alt =>
            {
                var doc = _session.Doc.Clone();
                doc.Blocks[block].Kind = new BlockKind.Image(img.Data with { Alt = alt });
                _session.ReplaceDoc(doc, undo: true);
                CloseCard();
            },
            CloseCard);
        CenterCard(card);
        FocusFirstField(card);
    }

    private static void FocusFirstField(FrameworkElement card)
    {
        card.Loaded += (_, _) =>
        {
            if (FindFirst<TextBox>(card) is { } box)
            {
                _ = box.Focus(FocusState.Programmatic);
            }
        };
    }

    private static T? FindFirst<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit)
            {
                return hit;
            }

            if (FindFirst<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ link and image popovers

    /// <summary>After a click or a movement key: the popover of the link the caret is in, or of the selected image.</summary>
    internal void ShowContextPopovers()
    {
        if (_vm is null || _session is null || _vm.Plain)
        {
            return;
        }

        var t = _vm.Toolbar;
        if (t.Link is { } url && _session.Sel.IsCollapsed)
        {
            if (!string.Equals(_linkPopoverFor, url, StringComparison.Ordinal))
            {
                ClosePopover();
                _linkPopoverFor = url;
                SetPopover(EditorCards.LinkPopover(url, OpenLinkDialog, OpenLink, CopyLinkAddress, () =>
                {
                    _session.SetLink(string.Empty, string.Empty);
                    ClosePopover();
                }));
            }

            return;
        }

        if (t.OnImage && _session.Sel.IsCollapsed)
        {
            ClosePopover();
            _linkPopoverFor = "image";
            SetPopover(ImagePopover());
            return;
        }

        ClosePopover();
    }

    private FrameworkElement ImagePopover()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(EditorUi.Action("imagepop.alt", "Alt text", OpenAltDialog));
        row.Children.Add(EditorUi.Action("imagepop.original", "Original", () => ResizeSelectedImage(0)));
        row.Children.Add(EditorUi.Action("imagepop.small", "Small", () => ResizeSelectedImage(240)));
        row.Children.Add(EditorUi.Action("imagepop.medium", "Medium", () => ResizeSelectedImage(480)));
        return EditorUi.Card(row);
    }

    private void SetPopover(FrameworkElement card)
    {
        _popover = card;
        AutomationProperties.SetAutomationId(card, "editor.popover");
        _overlay.Children.Add(card);
        UpdateOverlays();
    }

    private void ClosePopover()
    {
        if (_popover is null)
        {
            return;
        }

        _overlay.Children.Remove(_popover);
        _popover = null;
        _linkPopoverFor = null;
    }

    // ------------------------------------------------------------------ @ mentions

    /// <summary>After typing: the "@query" before the caret opens the list of recipients (G-075); nobody matching closes it.</summary>
    internal void UpdateMentions()
    {
        if (_vm?.Mentions is not { } source || _session is null || !_session.Sel.IsCollapsed)
        {
            CloseMentions();
            return;
        }

        var focus = _session.Sel.Focus;
        var kind = _session.Doc.Blocks[focus.B].Kind;
        var trigger = kind is BlockKind.CodeBlock ? null : MentionTrigger.Find(_session.Doc.Blocks[focus.B].Text, focus.O);
        var items = trigger is { } t ? source.Candidates(t.Query) : [];
        if (trigger is null || items.Count == 0)
        {
            CloseMentions();
            return;
        }

        _mentionStart = trigger.Value.Start;
        _mentionItems = items;
        _mentionSelected = Math.Min(_mentionSelected, items.Count - 1);
        ShowMentionList();
    }

    private void ShowMentionList()
    {
        if (_mentionList is not null)
        {
            _overlay.Children.Remove(_mentionList);
        }

        _mentionList = EditorCards.Mentions(_mentionItems, _mentionSelected, PickMention);
        _overlay.Children.Add(_mentionList);
        UpdateOverlays();
    }

    private void CloseMentions()
    {
        if (_mentionList is not null)
        {
            _overlay.Children.Remove(_mentionList);
            _mentionList = null;
        }

        _mentionStart = -1;
        _mentionSelected = 0;
    }

    private bool MentionKey(EditorKeyInput input)
    {
        if (_mentionList is null || input.Ctrl || input.Alt || input.Nav is null)
        {
            return false;
        }

        switch (input.Nav)
        {
            case Barbican.Editor.Layout.Input.NavKey.Down:
                _mentionSelected = (_mentionSelected + 1) % _mentionItems.Count;
                ShowMentionList();
                return true;
            case Barbican.Editor.Layout.Input.NavKey.Up:
                _mentionSelected = (_mentionSelected + _mentionItems.Count - 1) % _mentionItems.Count;
                ShowMentionList();
                return true;
            case Barbican.Editor.Layout.Input.NavKey.Enter or Barbican.Editor.Layout.Input.NavKey.Tab when !input.Shift:
                PickMention(_mentionItems[_mentionSelected]);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Replaces the typed "@que" with "@First " and tells the source (it may add the person as a recipient).</summary>
    internal void PickMention(MentionCandidate candidate)
    {
        if (_session is null || _mentionStart < 0)
        {
            return;
        }

        var focus = _session.Sel.Focus;
        var text = "@" + MentionTrigger.Label(candidate) + " ";
        var block = _session.Doc.Blocks[focus.B];
        var spaceFollows = focus.O < block.Length && char.IsWhiteSpace(block.Text[focus.O]);
        var start = _mentionStart;
        CloseMentions();
        _session.ReplaceRange(new Pos(focus.B, start), focus, spaceFollows ? text.TrimEnd() : text);
        _session.SetCaret(new Pos(focus.B, start + text.Length - (spaceFollows ? 1 : 0)));
        _vm?.Mentions?.Picked(candidate);
        FocusEditor();
    }
}
