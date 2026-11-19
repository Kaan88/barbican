// Mirrors Bulwark components/mail/reply-dock.tsx:40-90,170-211.
using Barbican.Core.Storage;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Barbican.App.Shell;

public sealed partial class ShellPage
{
    private const string DockShareKey = "replyDockShare";
    private double _dockShare = ViewHost.Get<IPrefsStore>().Current.Number(DockShareKey, ReplyDockLayout.DefaultShare);
    private uint? _dockPointer;
    private double _dockStartY;
    private double _dockStartHeight;

    private void OnDockPress(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ReaderColumn);
        if (_dockPointer.HasValue || !point.Properties.IsLeftButtonPressed || !DockResize.CapturePointer(e.Pointer)) return;
        _dockPointer = e.Pointer.PointerId;
        _dockStartY = point.Position.Y;
        _dockStartHeight = ComposerDockHost.ActualHeight;
        DockResize.Focus(FocusState.Pointer);
        e.Handled = true;
    }

    private void OnDockMove(object sender, PointerRoutedEventArgs e)
    {
        if (_dockPointer != e.Pointer.PointerId) return;
        ResizeDock(_dockStartHeight + _dockStartY - e.GetCurrentPoint(ReaderColumn).Position.Y);
        e.Handled = true;
    }

    private void OnDockRelease(object sender, PointerRoutedEventArgs e)
    {
        if (_dockPointer != e.Pointer.PointerId) return;
        _dockPointer = null;
        DockResize.ReleasePointerCapture(e.Pointer);
        SaveDockShare();
        e.Handled = true;
    }

    private void OnDockCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dockPointer == e.Pointer.PointerId) _dockPointer = null;
    }

    private void OnDockReset(object sender, DoubleTappedRoutedEventArgs e)
    {
        ResetDockShare();
        e.Handled = true;
    }

    private void OnDockResizeKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Home) ResetDockShare();
        else if (e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            ResizeDock(ComposerDockHost.ActualHeight + (e.Key == VirtualKey.Up ? 16 : -16));
            SaveDockShare();
        }
        else return;
        e.Handled = true;
    }

    private void ResizeDock(double height)
    {
        var pane = ReaderColumn.ActualHeight;
        if (pane < ReplyDockLayout.ShareMinPane) return;
        _dockShare = Math.Clamp(height, ReplyDockLayout.MinDock, pane - ReplyDockLayout.MinNativeReader) / pane;
        ApplyDockLayout();
    }

    private void SaveDockShare() => ViewHost.Get<IPrefsEditor>().Put(DockShareKey, _dockShare);

    private void ResetDockShare()
    {
        _dockShare = ReplyDockLayout.DefaultShare;
        ViewHost.Get<IPrefsEditor>().Put(DockShareKey, null);
        ApplyDockLayout();
    }
}
