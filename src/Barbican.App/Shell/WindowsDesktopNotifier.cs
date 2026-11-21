#if WINDOWS
// Mirrors: Bulwark hooks/use-app-badge.ts:34 (the app icon carries the unread count; 0 clears it), lib/notification-sound.ts:8 (five tones), lib/desktop/mail-notifications.ts:164-200 (attention: true flashes the taskbar button).
// What works without package identity and without touching the registry: the taskbar overlay badge (ITaskbarList3.SetOverlayIcon), the taskbar flash while the window is in the background (FlashWindowEx), the tone (MessageBeep).
// Not done here: system toast banners. A toast from an unpackaged app needs AppNotificationManager.Register(), which writes an AppUserModelId entry under HKCU (an OS change that needs the user's OK; M6b notifications report).
using System.Runtime.InteropServices;
using Barbican.Core.Notifications;
using SkiaSharp;

namespace Barbican.App.Shell;

internal sealed partial class WindowsDesktopNotifier : IDesktopNotifier
{
    private ITaskbarList3? _taskbar;
    private nint _icon;

    public event EventHandler<NoticeActivatedEventArgs>? Activated
    {
        add { }
        remove { }
    }

    public void Show(NewMailNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        OnWindow(hwnd =>
        {
            if (GetForegroundWindow() != hwnd)
            {
                var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = hwnd, Flags = FlashTray | FlashTimerNoForeground, Count = 3, Timeout = 0 };
                _ = FlashWindowEx(ref info);
            }
        });
        if (notice.PlaySound)
        {
            PlaySound(notice.SoundChoice);
        }
    }

    public void SetBadge(int count) => OnWindow(hwnd =>
    {
        try
        {
            _taskbar ??= (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"))!)!;
            _taskbar.HrInit();
            var old = _icon;
            _icon = count > 0 ? Render(count) : 0;
            _taskbar.SetOverlayIcon(hwnd, _icon, count > 0 ? $"{count} unread" : string.Empty);
            if (old != 0)
            {
                _ = DestroyIcon(old);
            }
        }
#pragma warning disable CA1031 // the taskbar is a courtesy: a shell that refuses the badge must not break mail
        catch (Exception)
#pragma warning restore CA1031
        {
            _taskbar = null;
        }
    });

    public void PlaySound(string choice)
    {
        // the five tones of Bulwark map to the system sounds that exist everywhere (no file is bundled)
        var kind = choice switch { "cheerful" => 0x40u, "swift" => 0x30u, "relax" => 0x10u, "default" => 0u, _ => 0x40u };
        _ = MessageBeep(kind);
    }

    private static void OnWindow(Action<nint> run)
    {
        if (ActiveWindow.Current is not { } window)
        {
            return;
        }

        var queue = window.DispatcherQueue;
        void Go() => run(WinRT.Interop.WindowNative.GetWindowHandle(window));
        if (queue.HasThreadAccess)
        {
            Go();
        }
        else
        {
            _ = queue.TryEnqueue(Go);
        }
    }

    // a 16 px red disc with the count; a PNG inside an icon resource is accepted by CreateIconFromResourceEx
    private static nint Render(int count)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var fill = new SKPaint { Color = new SKColor(0xDC, 0x26, 0x26), IsAntialias = true };
        canvas.DrawCircle(16, 16, 15, fill);
        var text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), text.Length > 2 ? 14 : text.Length > 1 ? 18 : 22);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var width = font.MeasureText(text);
        canvas.DrawText(text, 16 - (width / 2), 16 + (font.Size * 0.36f), SKTextAlign.Left, font, ink);
        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, 32, 32, 0);
    }

    private const uint FlashTray = 2;
    private const uint FlashTimerNoForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint kind);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial nint CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();

        void AddTab(nint hwnd);

        void DeleteTab(nint hwnd);

        void ActivateTab(nint hwnd);

        void SetActiveAlt(nint hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3
        void SetProgressValue(nint hwnd, ulong completed, ulong total);

        void SetProgressState(nint hwnd, int flags);

        void RegisterTab(nint hwndTab, nint hwndMdi);

        void UnregisterTab(nint hwndTab);

        void SetTabOrder(nint hwndTab, nint hwndInsertBefore);

        void SetTabActive(nint hwndTab, nint hwndMdi, uint reserved);

        void ThumbBarAddButtons(nint hwnd, uint count, nint buttons);

        void ThumbBarUpdateButtons(nint hwnd, uint count, nint buttons);

        void ThumbBarSetImageList(nint hwnd, nint imageList);

        void SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }
}
#endif
