using Barbican.Automation.Protocol;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Barbican.Automation;

/// <summary>
/// The fail-closed popup guard of AUTOMATION builds (docs/adr/0005 and 0007 "M0 review"; policy in
/// <see cref="PopupGuardPolicy"/>). It sweeps the open popups of the window
/// (<c>VisualTreeHelper.GetOpenPopupsForXamlRoot</c>) before and after every automation command and
/// on a 10 ms UI-thread timer in between, and closes every popup whose
/// <c>IsConstrainedToRootBounds</c> is false: such a popup is its own top-level window, which Windows
/// clamps onto a real monitor although the app window is off-screen. The command that was running
/// (or the next one, for a popup the timer caught) fails with <c>error unconstrained-popup TYPE</c>.
/// <para>The timer does not render anything, so an idle window still produces no frames. On the Uno
/// desktop head every popup is drawn inside the window, so the guard has nothing to check there.</para>
/// </summary>
internal sealed class PopupGuard
{
    private readonly AutomationContext _context;
    private DispatcherQueueTimer? _timer;
    private string? _pending;

    public PopupGuard(AutomationContext context)
    {
        _context = context;
    }

    /// <summary>Popups the guard closed since start (the guard test reads it through <c>popup-guard</c>).</summary>
    public int Closed { get; private set; }

    public void Start()
    {
        _timer = _context.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(10);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            if (Sweep() is { } error)
            {
                _pending ??= error;
            }
        };
        _timer.Start();
    }

    /// <summary>Checks the open popups now (UI thread); closes the unconstrained ones and returns the error, if any.</summary>
    public string? Sweep()
    {
        var root = _context.Root?.XamlRoot;
        if (root is null)
        {
            return null;
        }

        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root);
        if (popups.Count == 0)
        {
            return null;
        }

#if WINDOWS
        var seen = popups.Select(p => new OpenPopup(p.Child?.GetType().Name ?? nameof(Popup), p.IsConstrainedToRootBounds)).ToArray();
#else
        // Uno Skia draws every popup inside the window (no IsConstrainedToRootBounds there).
        var seen = popups.Select(p => new OpenPopup(p.Child?.GetType().Name ?? nameof(Popup), true)).ToArray();
#endif
        var (close, error) = PopupGuardPolicy.Evaluate(seen);
        foreach (var i in close)
        {
            popups[i].IsOpen = false;
            Closed++;
        }

        if (error is not null)
        {
            Console.Error.WriteLine("[automation] popup guard closed an unconstrained popup: " + error[PopupGuardPolicy.ErrorPrefix.Length..]);
        }

        return error;
    }

    /// <summary>The error of a popup the timer closed between commands, once.</summary>
    public string? TakePending()
    {
        var p = _pending;
        _pending = null;
        return p;
    }

    /// <summary>Snapshot for debugging: every open popup and whether it is constrained.</summary>
    public string Describe()
    {
        var root = _context.Root?.XamlRoot;
        if (root is null)
        {
            return "no xaml root";
        }

        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root);
#if WINDOWS
        var list = string.Join(", ", popups.Select(p => (p.Child?.GetType().Name ?? nameof(Popup)) + (p.IsConstrainedToRootBounds ? " constrained" : " UNCONSTRAINED")));
#else
        var list = string.Join(", ", popups.Select(p => p.Child?.GetType().Name ?? nameof(Popup)));
#endif
        return $"open {popups.Count} closed {Closed}" + (list.Length > 0 ? ": " + list : string.Empty);
    }
}
