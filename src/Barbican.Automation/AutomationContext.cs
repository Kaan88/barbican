using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Barbican.Automation;

/// <summary>The window under automation and the helpers every command uses (UI-thread hops, the clock).</summary>
internal sealed class AutomationContext
{
    public AutomationContext(Window window)
    {
        Window = window;
        Dispatcher = window.DispatcherQueue;
#if WINDOWS
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
#endif
    }

    public Window Window { get; }

    public DispatcherQueue Dispatcher { get; }

    /// <summary>The native window (Windows); 0 on the desktop head.</summary>
    public nint Hwnd { get; }

    public FrameworkElement? Root => Window.Content as FrameworkElement;

    /// <summary>Milliseconds on the Stopwatch clock (the probes' time base).</summary>
    public static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>Runs <paramref name="work"/> on the UI thread and returns its reply.</summary>
    public Task<string> OnUiAsync(Func<Task<string>> work)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Dispatcher.TryEnqueue(() => _ = RunAsync(work, tcs)))
        {
            tcs.TrySetResult("error no dispatcher");
        }

        return tcs.Task;
    }

    private static async Task RunAsync(Func<Task<string>> work, TaskCompletionSource<string> reply)
    {
        try
        {
            reply.TrySetResult(await work());
        }
#pragma warning disable CA1031 // every failure of a command becomes its one-line "error ..." reply
        catch (Exception e)
#pragma warning restore CA1031
        {
            reply.TrySetResult("error " + e.GetType().Name + ": " + e.Message);
        }
    }
}
