using Barbican.Automation.Protocol;
#if WINDOWS
using Barbican.Automation.Windows;
#endif

namespace Barbican.Automation;

/// <summary>
/// Routes one request line (spec/automation-protocol.md). UIA-client commands (<c>click</c>,
/// <c>dclick</c>, <c>uia-*</c>, <c>type</c>'s value write) run on the server thread: a UIA call from
/// the UI thread into the app's own providers would deadlock. Everything else runs on the UI thread.
/// The popup guard sweeps after every command and its error replaces the command's reply.
/// </summary>
internal sealed partial class CommandRouter : IDisposable
{
    private readonly AutomationContext _context;
    private readonly PopupGuard _guard;
    private readonly Dictionary<string, IAutomationCommand> _extensions;
    private readonly FrameProbe _probe;
    private readonly Burst _burst;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _closeTimer;
#if WINDOWS
    private readonly Uia _uia;
#endif

    public CommandRouter(AutomationContext context, PopupGuard guard, IEnumerable<IAutomationCommand> extensions)
    {
        _context = context;
        _guard = guard;
        _extensions = extensions.ToDictionary(c => c.Name, StringComparer.Ordinal);
        _probe = new FrameProbe(context);
        _burst = new Burst(context);
#if WINDOWS
        _uia = new Uia(context.Hwnd, Event);
#endif
    }

    /// <summary>The input commands whose timestamps go into the probe and burst event logs.</summary>
    private static readonly HashSet<string> Inputs = new(StringComparer.Ordinal)
    {
        "click", "rclick", "dclick", "peer-click", "move", "key", "scroll", "drag", "type", "invoke", "select", "text",
    };

    public async Task<string> DispatchAsync(string raw, CancellationToken cancellationToken)
    {
        var line = CommandLine.Parse(raw);
        string reply;
        if (_extensions.TryGetValue(line.Name, out var extension))
        {
            if (Inputs.Contains(line.Name))
            {
                Event(raw);
            }

            reply = await _context.OnUiAsync(() => extension.RunAsync(line.Argument, cancellationToken));
        }
        else
        {
            reply = await RouteAsync(line, raw, cancellationToken);
        }

        var guardError = await _context.OnUiAsync(() => Task.FromResult(_guard.Sweep() ?? _guard.TakePending() ?? string.Empty));
        return PopupGuardPolicy.Reply(reply, guardError.Length > 0 ? guardError : null);
    }

    public void Dispose()
    {
        _closeTimer?.Stop();
        _closeTimer = null;
#if WINDOWS
        _uia.Dispose();
#endif
    }

    /// <summary>Records an input event for a running probe and burst.</summary>
    public void Event(string command)
    {
        _probe.Event(command);
        _burst.Event(command);
    }

    private async Task<string> RouteAsync(CommandLine line, string raw, CancellationToken cancellationToken)
    {
#if WINDOWS
        switch (line.Name)
        {
            case "click":
            case "dclick":
            {
                // A point in the web area is a DOM click (the WebView2 is a separate HWND, UIA cannot see into it reliably).
                var web = await _context.OnUiAsync(() => WebClickAsync(line, string.Equals(line.Name, "dclick", StringComparison.Ordinal) ? 2 : 1));
                if (!string.Equals(web, NotWeb, StringComparison.Ordinal))
                {
                    return web;
                }

                var r = await Task.Run(() => _uia.Run("uia-" + raw), cancellationToken);
                return await AfterUiaAsync(r, plain: true);
            }

            case "type":
            {
                var id = await _context.OnUiAsync(() => Task.FromResult(FocusedId()));
                return string.IsNullOrEmpty(id)
                    ? "nothing focused"
                    : await Task.Run(() => _uia.Run($"uia-append {id} {line.Argument}"), cancellationToken);
            }
        }

        if (line.Name.StartsWith("uia-", StringComparison.Ordinal))
        {
            var r = await Task.Run(() => _uia.Run(raw), cancellationToken);
            return await AfterUiaAsync(r, plain: false);
        }
#endif
        return await _context.OnUiAsync(() => RunOnUiAsync(line, raw));
    }

#if WINDOWS
    private async Task<string> AfterUiaAsync(string reply, bool plain)
    {
        if (reply.StartsWith("focus:", StringComparison.Ordinal))
        {
            var id = reply[6..];
            reply = await _context.OnUiAsync(() => Task.FromResult(FocusById(id)));
        }

        return plain && reply.StartsWith("ok ", StringComparison.Ordinal) ? "ok" : reply;
    }
#endif
}
