// Mirrors: Rust src/rte/mod.rs on_key :517, on_pointer :640, right_down :381; ADR 0006 section 6 (the hidden TextBox IME path: committed text is forwarded into the document, TextComposition events draw the
// preedit inline); Bulwark rich-text-editor.tsx (Alt+F10 toolbar focus, Escape order B-RTE-029). The keyboard and pointer reach the editor through the same entry points the automation probes call.

using Barbican.App.Shell;
using Barbican.Core.Editor;
using Barbican.Editor.Layout.Input;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Editor;
using Barbican.Presentation.Menus;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Barbican.App.Views.Editor;

public sealed partial class EditorView
{
    private bool _pointerDown;
    private int _imageDrag = -1;

    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void WireEvents()
    {
        _spacer.PointerPressed += OnPointerPressed;
        _spacer.PointerMoved += OnPointerMoved;
        _spacer.PointerReleased += OnPointerReleased;
        _spacer.PointerCanceled += (_, _) => EndPointer();
        _spacer.PointerCaptureLost += (_, _) => EndPointer();
        _spacer.DragOver += OnDragOver;
        _spacer.Drop += OnDrop;
        _scroll.ViewChanged += (_, _) =>
        {
            _canvas.Redraw();
            UpdateOverlays();
        };
        _body.SizeChanged += (_, _) =>
        {
            _canvas.Width = _body.ActualWidth;
            _canvas.Height = _body.ActualHeight;
            _toolbar.Fit(_body.ActualWidth - 32);
            Refresh(scrollToCaret: false);
        };
        _ime.TextChanged += OnImeTextChanged;
        _ime.TextCompositionStarted += (_, _) => _composing = true;
        _ime.TextCompositionChanged += (_, _) => ShowPreedit(_ime.Text);
        _ime.TextCompositionEnded += (_, _) => CommitComposition();
        _ime.GotFocus += (_, _) =>
        {
            if (_vm is not null)
            {
                ViewHost.Find<ActiveEditor>()?.Activate(_vm);
            }
        };
        _source.TextChanged += (_, _) =>
        {
            if (!_settingSource)
            {
                _vm?.EditSource(_source.Text);
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        _toolbar.CommandInvoked += OnToolbarCommand;
        _bubble.CommandInvoked += OnToolbarCommand;
        _toolbar.PickerRequested += OnPickerRequested;
        _bubble.PickerRequested += OnPickerRequested;
        _toolbar.ReturnFocus += FocusEditor;
        _bubble.ReturnFocus += FocusEditor;
    }

    private void OnToolbarCommand(EditorCommand command, string? argument)
    {
        _ = _vm?.Execute(command, argument);
        if (command is not (EditorCommand.Link or EditorCommand.ToggleSource))
        {
            FocusEditor();
        }
    }

    private void OnImeTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_composing || _ime.Text.Length == 0)
        {
            return;
        }

        var text = _ime.Text;
        _ime.Text = string.Empty;
        TypeText(text);
    }

    /// <summary>Text that arrives from the keyboard, an IME commit or the automation <c>editor-type</c>: one entry point.</summary>
    internal void TypeText(string text)
    {
        if (_ctl is null || _session is null || _vm?.SourceOpen == true)
        {
            return;
        }

        if (_ctl.TypeText(text))
        {
            UpdateMentions();
        }
    }

    private void ShowPreedit(string text)
    {
        if (_layout is null || _session is null)
        {
            return;
        }

        _layout.SetPreedit(_session.Sel.Focus, text);
        Refresh(scrollToCaret: true);
    }

    private void CommitComposition()
    {
        var text = _ime.Text;
        _composing = false;
        _layout?.SetPreedit(null, null);
        _ime.Text = string.Empty;
        if (text.Length > 0)
        {
            TypeText(text);
        }
        else
        {
            Refresh(scrollToCaret: true);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_vm is null || _vm.SourceOpen)
        {
            return;
        }

        if (e.Key == VirtualKey.Escape && CloseTransient())
        {
            e.Handled = true;
            return;
        }

        if (!ReferenceEquals(e.OriginalSource, _ime))
        {
            return;
        }

        var ctrl = Down(VirtualKey.Control);
        var shift = Down(VirtualKey.Shift);
        var alt = Down(VirtualKey.Menu);
        if (alt && e.Key == VirtualKey.F10)
        {
            _toolbar.FocusFirst();
            e.Handled = true;
            return;
        }

        if (!_composing && EditorKeyInput.FromVirtualKey((int)e.Key, ctrl, shift, alt) is { } input)
        {
            e.Handled = HandleKey(input);
        }
    }

    /// <summary>One key of the editor: a shortcut command, a mention-list key, or navigation and editing. True when handled.</summary>
    internal bool HandleKey(EditorKeyInput input)
    {
        if (_ctl is null || _vm is null || _session is null)
        {
            return false;
        }

        if (MentionKey(input))
        {
            return true;
        }

        var command = EditorKeyMap.Resolve(input.Key, _vm.Plain, out var swallowed);
        if (command is { } c)
        {
            _ = _vm.Execute(c);
            _linkPopoverFor = null;
            return true;
        }

        if (swallowed)
        {
            return true;
        }

        if (input.Nav is { } nav)
        {
            var handled = _ctl.Key(nav, input.Ctrl, input.Shift, input.Alt);
            if (handled)
            {
                if (nav is NavKey.Left or NavKey.Right or NavKey.Up or NavKey.Down or NavKey.Home or NavKey.End or NavKey.PageUp or NavKey.PageDown)
                {
                    ShowContextPopovers();
                }
                else
                {
                    UpdateMentions();
                }
            }

            return handled;
        }

        return false;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_ctl is null || _vm is null)
        {
            return;
        }

        CloseTransient();
        var point = e.GetCurrentPoint(_spacer);
        var (x, y) = ((float)(point.Position.X - PadX), (float)(point.Position.Y - PadY));
        _ = _ime.Focus(FocusState.Programmatic);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            OpenContextMenu(x, y, e.GetCurrentPoint(null).Position);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        PointerDown(x, y, Down(VirtualKey.Shift));
        _ = _spacer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    /// <summary>A left press at a document point (also the automation <c>editor-click</c>).</summary>
    internal void PointerDown(float x, float y, bool shift)
    {
        if (_ctl is null || _layout is null || _session is null)
        {
            return;
        }

        var hit = _layout.HitTest(x, y);
        var focus = _session.Sel.Focus.B;
        var onSelectedImage = hit.Pos.B == focus && _layout.ImageRect(hit.Pos.B) is { } r && x >= r.X && x <= r.X + r.Width && y >= r.Y && y <= r.Y + r.Height && _session.Sel.IsCollapsed;
        if (onSelectedImage)
        {
            _imageDrag = hit.Pos.B;
            _pointerDown = true;
            return;
        }

        _ctl.PointerDown(x, y, shift);
        _pointerDown = true;
        ShowContextPopovers();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDown || _ctl is null)
        {
            return;
        }

        var p = e.GetCurrentPoint(_spacer).Position;
        if (_imageDrag >= 0)
        {
            return;
        }

        _ctl.PointerDrag((float)(p.X - PadX), (float)(p.Y - PadY));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_spacer).Position;
        PointerUp((float)(p.X - PadX), (float)(p.Y - PadY));
        _spacer.ReleasePointerCapture(e.Pointer);
    }

    /// <summary>The button went up at a document point (finishes a drag, or an image move).</summary>
    internal void PointerUp(float x, float y)
    {
        if (_imageDrag >= 0 && _layout is not null && _session is not null)
        {
            var from = _imageDrag;
            _imageDrag = -1;
            var (to, _) = _layout.HitTest(x, y);
            if (to.B != from)
            {
                MoveImage(from, to);
            }
        }

        EndPointer();
    }

    private void EndPointer()
    {
        _pointerDown = false;
        _imageDrag = -1;
        _ctl?.PointerUp();
        UpdateOverlays();
    }

    /// <summary>Moves an image block to another place: it is cut where it was and inserted at the drop position (I-265, image drag).</summary>
    internal void MoveImage(int from, Pos to)
    {
        if (_session is null || _session.Doc.Blocks[from].Kind is not BlockKind.Image img)
        {
            return;
        }

        var data = img.Data;
        _session.Checkpoint(EditKind.Other);
        _session.SelectBlock(from);
        _session.Backspace(word: false);
        var target = to.B > from ? to with { B = Math.Max(0, to.B - 1) } : to;
        _session.SetCaret(target);
        _session.InsertImage(data);
    }

    private void OpenContextMenu(float x, float y, Windows.Foundation.Point window)
    {
        if (_ctl is null || _vm is null)
        {
            return;
        }

        var pos = _ctl.RightDown(x, y);
        _vm.ContextWord = _vm.MisspelledAt(pos.B, pos.O);
        _vm.RefreshToolbar();
        ViewHost.Find<ActiveEditor>()?.Activate(_vm);
        ViewHost.Find<IContextMenuService>()?.Show(
            new MenuContext(EditorMenus.Menu, [], null, null, null, null, CommandSurface.ContextMenu),
            new MenuAnchor(window.X, window.Y, null));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_vm?.Plain != true && e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

#pragma warning disable VSTHRD100 // a drag-and-drop event handler
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm is null || _session is null || _vm.Plain || !e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var file in await OsClipboardBackend.ReadFilesAsync(items))
            {
                EditorClipboardActions.InsertImage(_session, file.Bytes, file.Mime, file.Name, ImageSize);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

#pragma warning restore VSTHRD100

    private static (int Width, int Height)? ImageSize(byte[] bytes) => Barbican.Editor.Layout.LayoutImages.Decode(bytes) is { } img ? (img.Width, img.Height) : null;
}
